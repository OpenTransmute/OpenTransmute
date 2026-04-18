using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Inventory;
using OpenTransmute.Jobs;
using OpenTransmute.Llm;
using OpenTransmute.Models;
using OpenTransmute.Writing;
using OpenTransmute.Parsing;

namespace OpenTransmute.Orchestration;

/// <summary>
/// Single orchestrator that handles all three operations (Decompose, Compose, Implement)
/// against all backends (ClaudeCode, OpenAI, Ollama) via <see cref="ILlmExecutor"/>.
/// Produces a unified <see cref="PhaseEvent"/> stream consumed by <see cref="JobRunner"/>.
/// </summary>
public sealed class JobOrchestrator(
    PromptTemplates promptTemplates,
    PromptBuilder promptBuilder,
    OutputWriter outputWriter,
    IEnumerable<ILlmExecutor> executors,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<JobOrchestrator> logger)
{
    #region Members

    private static readonly TimeSpan[] RetryBackoffs =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120)
    ];

    private static readonly Regex FenceRegex =
        new(@"^```[^\n]*\n([\s\S]*?)```\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly HashSet<char> InvalidFileNameChars =
        new HashSet<char>(Path.GetInvalidFileNameChars());

    #endregion

    #region Methods

    /// <summary>
    /// Dispatches execution based on the runtime type of <paramref name="job"/>.
    /// Returns an async stream of <see cref="PhaseEvent"/> values consumed by <see cref="JobRunner"/>.
    /// </summary>
    public IAsyncEnumerable<PhaseEvent> RunAsync(JobBase job, CancellationToken ct) => job switch
    {
        DecomposeJob dj  => RunDecomposeAsync(dj, ct),
        ComposeJob   cj  => RunComposeAsync(cj, ct),
        ImplementJob ij  => RunImplementAsync(ij, ct),
        _                => throw new InvalidOperationException($"Unsupported job type: {job.GetType().Name}")
    };

    // ── Decompose ─────────────────────────────────────────────────────────────

    /// <summary>Runs the multi-phase decompose pipeline.</summary>
    private async IAsyncEnumerable<PhaseEvent> RunDecomposeAsync(
        DecomposeJob job,
        [EnumeratorCancellation] CancellationToken ct)
    {
        DecomposeOptions options = job.Options;

        ILlmExecutor executor = ResolveExecutor(options.Orchestrator);

        RunContext context = new RunContext(
            options.ProjectName,
            job.LocalSourcePath ?? options.Source,
            options.OutputRoot,
            string.IsNullOrWhiteSpace(options.Hints) ? null : options.Hints.Trim());

        if (options.StartPhase > 0)
            context.LoadPriorOutputs(options.StartPhase);

        foreach (PhaseSpec phase in promptTemplates.Phases.Where(p =>
            p.Number >= options.StartPhase &&
            (options.EndPhase == null || p.Number <= options.EndPhase)))
        {
            yield return new PhaseStarted(phase.Number, phase.Title);
            outputWriter.DeletePhaseFiles(options.OutputRoot, options.ProjectName, phase.Number);

            if (phase.IsExpansion)
            {
                await foreach (PhaseEvent evt in RunDecomposeExpansionPhaseAsync(phase, context, options, executor, ct))
                    yield return evt;
            }
            else
            {
                await foreach (PhaseEvent evt in RunDecomposeSimplePhaseAsync(phase, context, options, executor, ct))
                    yield return evt;
            }

            if (context.LastFailed) yield break;
        }
    }

    private async IAsyncEnumerable<PhaseEvent> RunDecomposeSimplePhaseAsync(
        PhaseSpec phase, RunContext context, DecomposeOptions options, ILlmExecutor executor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string prompt = promptBuilder.BuildSimplePrompt(phase, context);
        LlmExecutionContext ctx = BuildDecomposeContext(phase, prompt, options, context.SourcePath);

        (string? output, TokenUsage tokens, IReadOnlyList<string> lines, string? error) =
            await RunLlmCallWithRetryAsync(ctx, executor, ct);

        foreach (string line in lines)
            yield return new LogLine(phase.Number, line);

        if (tokens.Total > 0)
            yield return new LogLine(phase.Number,
                $"Phase {phase.Number}: {tokens.Total:N0} tokens" +
                (tokens.CostUsd.HasValue ? $" (~${tokens.CostUsd:F4})" : string.Empty));

        if (error is not null)
        {
            logger.LogError("Phase {N} failed: {Err}", phase.Number, error);
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, error);
            yield break;
        }

        string? outputPath = null;
        string? saveError = null;
        try
        {
            outputPath = await outputWriter.WriteAsync(
                options.OutputRoot, options.ProjectName, phase.OutputFilename,
                output!.TrimEnd(), ct);
            context.PriorOutputPaths[phase.OutputFilename] = outputPath;
            logger.LogInformation("Phase {N}: saved {Path}", phase.Number, outputPath);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            saveError = ex.Message;
        }

        if (saveError is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, saveError);
        }
        else
        {
            yield return new PhaseCompleted(phase.Number, outputPath, tokens);
        }
    }

    private async IAsyncEnumerable<PhaseEvent> RunDecomposeExpansionPhaseAsync(
        PhaseSpec phase, RunContext context, DecomposeOptions options, ILlmExecutor executor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new LogLine(phase.Number, $"Phase {phase.Number}: identifying groups...");

        // Step 1 — discovery
        string discoveryPrompt = promptBuilder.BuildExpansionDiscoveryPrompt(phase, context);
        LlmExecutionContext discoveryCtx = BuildDecomposeContext(phase, discoveryPrompt, options, context.SourcePath);

        (string? discoveryOutput, TokenUsage discoveryTokens, IReadOnlyList<string> discoveryLines, string? discoveryError) =
            await RunLlmCallWithRetryAsync(discoveryCtx, executor, ct);

        foreach (string line in discoveryLines)
            yield return new LogLine(phase.Number, line);

        if (discoveryTokens.Total > 0)
            yield return new LogLine(phase.Number,
                $"Phase {phase.Number} discovery: {discoveryTokens.Total:N0} tokens" +
                (discoveryTokens.CostUsd.HasValue ? $" (~${discoveryTokens.CostUsd:F4})" : string.Empty));

        if (discoveryError is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, discoveryError);
            yield break;
        }

        List<JsonElement>? items = null;
        string? parseError = null;
        try { items = ParseJsonArray(discoveryOutput!); }
        catch (Exception ex) { parseError = ex.Message; }

        if (parseError is not null)
        {
            logger.LogError("Phase {N}: discovery JSON parse failed: {Err}", phase.Number, parseError);
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, $"Discovery JSON parse failed: {parseError}");
            yield break;
        }

        yield return new LogLine(phase.Number, $"Found {items!.Count} groups. Speccing each...");

        IReadOnlyDictionary<string, string> expansionBasePaths = context.SnapshotPriorPaths();

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            JsonElement item = items[i];
            string itemName = GetString(item, "groupName") ?? GetString(item, "name") ?? $"item-{i + 1}";
            int index = i + 1;

            yield return new ExpansionItemStarted(phase.Number, index, itemName);

            string itemPrompt = promptBuilder.BuildExpansionItemPrompt(phase, context, item, expansionBasePaths);
            LlmExecutionContext itemCtx = BuildDecomposeContext(phase, itemPrompt, options, context.SourcePath);

            (string? itemOutput, TokenUsage itemTokens, IReadOnlyList<string> itemLines, string? itemError) =
                await RunLlmCallWithRetryAsync(itemCtx, executor, ct);

            foreach (string line in itemLines)
                yield return new LogLine(phase.Number, line);

            if (itemTokens.Total > 0)
                yield return new LogLine(phase.Number,
                    $"  Group '{itemName}': {itemTokens.Total:N0} tokens" +
                    (itemTokens.CostUsd.HasValue ? $" (~${itemTokens.CostUsd:F4})" : string.Empty));

            if (itemError is not null)
            {
                context.LastFailed = true;
                yield return new PhaseFailed(phase.Number, $"Group '{itemName}': {itemError}");
                yield break;
            }

            string filename = ResolveExpansionFilename(phase, index, itemName, options.ProjectName, string.Empty);
            string? itemOutputPath = null;
            string? itemSaveError = null;
            try
            {
                itemOutputPath = await outputWriter.WriteAsync(
                    options.OutputRoot, options.ProjectName, filename, itemOutput!.TrimEnd(), ct);
                context.PriorOutputPaths[Path.GetFileName(itemOutputPath)] = itemOutputPath;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                itemSaveError = ex.Message;
            }

            if (itemSaveError is not null)
            {
                context.LastFailed = true;
                yield return new PhaseFailed(phase.Number, $"Group '{itemName}' save failed: {itemSaveError}");
                yield break;
            }

            yield return new ExpansionItemCompleted(phase.Number, index, itemOutputPath!, itemTokens);
        }

        yield return new PhaseCompleted(phase.Number, null, discoveryTokens);
    }

    // ── Compose ───────────────────────────────────────────────────────────────

    /// <summary>Runs a single compose call against the selected LLM backend.</summary>
    private async IAsyncEnumerable<PhaseEvent> RunComposeAsync(
        ComposeJob job,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const int PhaseNum = 0;
        ComposeOptions options = job.Options;

        yield return new PhaseStarted(PhaseNum, "Compose");

        string prompt;

        if (!string.IsNullOrWhiteSpace(options.PrebuiltPrompt))
        {
            prompt = options.PrebuiltPrompt;
            string hintsSection = PromptBuilder.FormatUserHints(options.Hints);
            if (!string.IsNullOrEmpty(hintsSection))
                prompt = hintsSection + "\n\n---\n\n" + prompt;
            job.AssembledPrompt = prompt;
        }
        else
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            List<Models.InventoryItem> items = await db.InventoryItems
                .Where(i => options.SelectedItemIds.Contains(i.Id))
                .ToListAsync(ct);

            if (items.Count == 0)
            {
                yield return new PhaseFailed(PhaseNum, "No inventory items found for the selected IDs.");
                yield break;
            }

            job.AppendLog($"Composing from {items.Count} inventory items.");

            IEnumerable<string> markdownBlocks = items.Select(i =>
            {
                string securityTag = i.SecurityScore >= 7
                    ? $" ⚠ SECURITY:{i.SecurityScore}/10"
                    : i.SecurityScore >= 4
                        ? $" [Security:{i.SecurityScore}/10]"
                        : string.Empty;
                return $"### {i.Category}: {i.Name}{securityTag}\n\n{i.RawMarkdown}";
            });

            prompt = promptBuilder.BuildComposePrompt(
                markdownBlocks,
                options.TargetDescription,
                options.TargetEnvironment,
                options.TargetTechnology,
                options.Hints);
            job.AssembledPrompt = prompt;
        }

        job.AppendLog($"Engine: {options.Orchestrator}" +
            (options.Model is not null ? $" | Model: {options.Model}" : string.Empty));
        job.AppendLog($"Prompt length: {prompt.Length:N0} chars");

        ILlmExecutor executor = ResolveExecutor(options.Orchestrator);
        LlmExecutionContext ctx = BuildComposeContext(prompt, options);

        (string? output, TokenUsage tokens, IReadOnlyList<string> lines, string? error) =
            await RunLlmCallWithRetryAsync(ctx, executor, ct);

        foreach (string line in lines)
            yield return new LogLine(PhaseNum, line);

        if (error is not null)
        {
            yield return new PhaseFailed(PhaseNum, error);
            yield break;
        }

        job.Output = output!;

        string? outputPath = null;
        string? saveError = null;
        try
        {
            string outputDir = Path.Combine(options.OutputRoot, "Output", "Composition", options.OutputName);
            Directory.CreateDirectory(outputDir);
            outputPath = Path.Combine(outputDir, "compose-output.md");
            await File.WriteAllTextAsync(outputPath, output!, ct);
            job.AppendLog($"Output saved to: {outputPath}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            saveError = ex.Message;
        }

        if (saveError is not null)
        {
            yield return new PhaseFailed(PhaseNum, $"Save failed: {saveError}");
            yield break;
        }

        yield return new PhaseCompleted(PhaseNum, outputPath, tokens);
    }

    // ── Implement ─────────────────────────────────────────────────────────────

    /// <summary>Runs a single implement call: writes plan files then invokes the LLM to produce code.</summary>
    private async IAsyncEnumerable<PhaseEvent> RunImplementAsync(
        ImplementJob job,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const int PhaseNum = 0;
        ImplementOptions options = job.Options;

        yield return new PhaseStarted(PhaseNum, "Implement");

        string outputDir = options.OutputDirectory;
        Directory.CreateDirectory(outputDir);

        job.AppendLog($"Engine: {options.Orchestrator}" +
            (options.Model is not null ? $" | Model: {options.Model}" : " | Default model"));
        job.AppendLog($"Output: {outputDir}");

        string projectName = string.IsNullOrWhiteSpace(options.ProjectName)
            ? Path.GetFileName(outputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : options.ProjectName;

        string outDirSlash = outputDir.Replace("\\", "/");

        string planContent =
            $"""
            # Implementation Plan — {projectName}

            **Project name:** {projectName}
            **Output directory:** {outDirSlash}

            ---

            ## Mandatory Guards

            {promptTemplates.TransmuteGuards}

            ---

            ## Implementation Specification

            {options.SpecContent}

            ---

            ## Instructions

            You are implementing the project "{projectName}".
            Name the project, its root namespace, package, module, or equivalent top-level identifier exactly "{projectName}" throughout.

            Write all source code and configuration files to: {outDirSlash}

            - Apply every guard above without exception before writing any code.
            - Read the full specification before writing any file.
            - Create a complete, working implementation — not a skeleton or stub.
            - Implement every module, component, data model, and algorithm described.
            - Organise files logically for the target tech stack (e.g. src/, tests/, docs/).
            - When finished, write a brief DONE.md summarising what was implemented.
            """;

        // Write support files — capture errors outside try/catch so yield is valid
        string? fileWriteError = null;
        try
        {
            string specPath   = Path.Combine(outputDir, "_spec.md");
            string guardsPath = Path.Combine(outputDir, "_guards.md");
            string planPath   = Path.Combine(outputDir, "compose-plan.md");
            await File.WriteAllTextAsync(specPath,   options.SpecContent,              ct);
            await File.WriteAllTextAsync(guardsPath, promptTemplates.TransmuteGuards,  ct);
            await File.WriteAllTextAsync(planPath,   planContent,                      ct);
            job.AppendLog($"Plan written to: {planPath}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            fileWriteError = ex.Message;
        }

        if (fileWriteError is not null)
        {
            yield return new PhaseFailed(PhaseNum, $"Failed to write plan files: {fileWriteError}");
            yield break;
        }

        // Prompt for Claude Code path — tool-calling path uses planContent as user message
        string claudePrompt =
            $"""
            Read _guards.md first. It contains mandatory rules that govern everything you write — apply them without exception throughout this task.

            Then read _spec.md. It contains the complete implementation specification.

            You are implementing the project "{projectName}".
            Name the project, its root namespace, package, module, or equivalent top-level identifier exactly "{projectName}" throughout.

            Write all source code and configuration files to: {outDirSlash}

            - Read _guards.md and _spec.md before writing any code.
            - Create a complete, working implementation — not a skeleton or stub.
            - Implement every module, component, data model, and algorithm described.
            - Organise files logically for the target tech stack (e.g. src/, tests/, docs/).
            - When finished, write a brief DONE.md summarising what was implemented.
            """;

        ILlmExecutor executor = ResolveExecutor(options.Orchestrator);
        LlmExecutionContext ctx = BuildImplementContext(claudePrompt, planContent, options, outputDir);

        (string? output, TokenUsage tokens, IReadOnlyList<string> lines, string? error) =
            await RunLlmCallWithRetryAsync(ctx, executor, ct);

        foreach (string line in lines)
            yield return new LogLine(PhaseNum, line);

        if (error is not null)
        {
            yield return new PhaseFailed(PhaseNum, error);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(output))
            job.AppendLog($"Model response: {output[..Math.Min(300, output.Length)]}");

        string[] written = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("_") && Path.GetFileName(f) != "compose-plan.md")
            .Select(f => Path.GetRelativePath(outputDir, f))
            .ToArray();

        job.AppendLog($"Files written ({written.Length}):");
        foreach (string f in written)
            job.AppendLog($"  {f}");

        yield return new PhaseCompleted(PhaseNum, null, tokens);
    }

    // ── Core LLM execution ────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single LLM call with exponential-backoff retry on rate-limit errors.
    /// Buffers all events and returns them together so the caller can yield after the call.
    /// </summary>
    private async Task<(string? Output, TokenUsage Tokens, IReadOnlyList<string> LogLines, string? Error)>
        RunLlmCallWithRetryAsync(LlmExecutionContext ctx, ILlmExecutor executor, CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            List<string> lines  = new();
            string? output      = null;
            TokenUsage tokens   = TokenUsage.Zero;
            string? error       = null;
            bool rateLimited    = false;
            TimeSpan retryWait  = RetryBackoffs[0];

            try
            {
                await foreach (LlmOutputEvent evt in executor.ExecuteAsync(ctx, ct))
                {
                    switch (evt)
                    {
                        case LlmLine l:      lines.Add(l.Text);               break;
                        case LlmCompleted c: output = c.Output; tokens = c.Tokens; break;
                        case LlmFailed f:    error = f.Error;                  break;
                    }
                }
            }
            catch (LlmRateLimitException ex) when (attempt < RetryBackoffs.Length)
            {
                rateLimited = true;
                retryWait   = TimeSpan.FromSeconds(
                    Math.Max(ex.RetryAfter.TotalSeconds, RetryBackoffs[attempt].TotalSeconds));
            }
            catch (LlmRateLimitException)
            {
                return (null, TokenUsage.Zero, lines, "Rate limit retries exhausted.");
            }

            if (rateLimited)
            {
                logger.LogWarning("Rate limit hit. Waiting {Wait}s before retry {Attempt}/{Max}",
                    retryWait.TotalSeconds, attempt + 1, RetryBackoffs.Length);
                await Task.Delay(retryWait, ct);
                attempt++;
                continue;
            }

            return (output, tokens, lines, error);
        }
    }

    // ── Context builders ──────────────────────────────────────────────────────

    private LlmExecutionContext BuildDecomposeContext(
        PhaseSpec phase, string prompt, DecomposeOptions options, string sourcePath)
    {
        int weightDefault = phase.ModelWeight switch
        {
            "thick" => options.ThickMaxOutputTokens,
            "thin"  => options.ThinMaxOutputTokens,
            _       => options.RegularMaxOutputTokens
        };
        int maxTokens = options.MaxOutputTokens == 0
            ? weightDefault
            : Math.Min(options.MaxOutputTokens, weightDefault);

        string model = ResolveModel(phase.ModelWeight, options);

        return new LlmExecutionContext
        {
            UserPrompt              = prompt,
            Model                   = model,
            ApiKey                  = options.OpenAiApiKey,
            Endpoint                = options.OpenAiEndpoint,
            // Claude subprocess needs the source directory as its working directory so
            // read-only file tools (Read, Glob, Grep, LS) can browse the repository.
            WorkingDirectory        = sourcePath,
            MaxTurns                = options.MaxTurns,
            MaxOutputTokens         = maxTokens,
            Timeout                 = TimeSpan.FromMinutes(options.TimeoutMinutes),
            EnableReadOnlyFileTools = true,
            EnableFileTools         = true,
            IgnoreContent = promptTemplates.TransmuteIgnore,
            FileToolsRoot = sourcePath
        };
    }

    private static LlmExecutionContext BuildComposeContext(string prompt, ComposeOptions options) =>
        new()
        {
            UserPrompt      = prompt,
            Model           = options.Model,
            ApiKey          = options.OpenAiApiKey,
            Endpoint        = options.OpenAiEndpoint,
            MaxOutputTokens = options.MaxOutputTokens,
            Timeout         = TimeSpan.FromMinutes(options.TimeoutMinutes),
            EnableFileTools = false
        };

    private LlmExecutionContext BuildImplementContext(
        string claudePrompt, string planContent, ImplementOptions options, string outputDir) =>
        new()
        {
            UserPrompt       = options.Orchestrator == OrchestratorType.ClaudeCode ? claudePrompt : planContent,
            Model            = options.Model,
            ApiKey           = options.OpenAiApiKey,
            Endpoint         = options.OpenAiEndpoint,
            WorkingDirectory = outputDir,
            MaxTurns         = options.MaxTurns,
            MaxOutputTokens  = 0,
            Timeout          = TimeSpan.FromMinutes(options.TimeoutMinutes),
            EnableFileTools  = options.Orchestrator != OrchestratorType.ClaudeCode,
            FileToolsRoot    = outputDir,
            IgnoreContent    = promptTemplates.TransmuteIgnore
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ILlmExecutor ResolveExecutor(OrchestratorType type) =>
        executors.FirstOrDefault(e => e.BackendType == type)
            ?? throw new InvalidOperationException($"No ILlmExecutor registered for {type}.");

    private static string ResolveModel(string modelWeight, DecomposeOptions options)
    {
        string regular = options.RegularModel ?? string.Empty;
        return modelWeight switch
        {
            "thick" => options.ThickModel   ?? regular,
            "thin"  => options.ThinModel    ?? regular,
            _       => regular
        };
    }

    private static List<JsonElement> ParseJsonArray(string raw)
    {
        string json = ExtractJsonArray(raw.Trim());
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Discovery response is not a JSON array.");
        return doc.RootElement.EnumerateArray()
            .Select(e => JsonDocument.Parse(e.GetRawText()).RootElement.Clone())
            .ToList();
    }

    private static string ExtractJsonArray(string text)
    {
        Match fence = FenceRegex.Match(text);
        if (fence.Success)
        {
            string fenced = fence.Groups[1].Value.Trim();
            if (fenced.StartsWith('[')) return fenced;
        }

        if (text.StartsWith('[')) return text;

        int start = text.IndexOf('[');
        if (start >= 0)
        {
            int depth = 0; bool inStr = false; bool esc = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (esc)              { esc = false; continue; }
                if (c == '\\' && inStr) { esc = true; continue; }
                if (c == '"')         { inStr = !inStr; continue; }
                if (inStr)            continue;
                if (c is '[' or '{')  depth++;
                else if (c is ']' or '}') { depth--; if (depth == 0) return text[start..(i + 1)]; }
            }
        }

        throw new InvalidOperationException(
            "Discovery response contains no JSON array. " +
            $"Response preview: {text[..Math.Min(200, text.Length)]}");
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return element.TryGetProperty(name, out JsonElement prop)
            ? (prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString())
            : null;
    }

    private static string Slug(string input)
    {
        char[] chars = new char[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            chars[i] = InvalidFileNameChars.Contains(c) ? '-' : char.ToLowerInvariant(c);
        }
        string cleaned = new string(chars);
        return string.Join("-", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    }

    private static string ResolveExpansionFilename(
        PhaseSpec phase, int index, string itemName, string projectName, string sourcePath)
    {
        string pattern = phase.OutputPattern
            ?? throw new InvalidOperationException($"Phase {phase.Number} has no OutputPattern.");

        string full = pattern
            .Replace("{slug}",     Slug(itemName))
            .Replace("{index:00}", index.ToString("00"))
            .Replace("<project>",  projectName, StringComparison.OrdinalIgnoreCase)
            .Replace("<path>",     sourcePath,  StringComparison.OrdinalIgnoreCase);

        return Path.GetFileName(full);
    }

    #endregion
}
