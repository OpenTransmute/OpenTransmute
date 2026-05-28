using System.Runtime.CompilerServices;
using System.Text;
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

    private static readonly JsonSerializerOptions _verifyJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

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
        VerifyJob    vj  => RunVerifyAsync(vj, ct),
        _                => throw new InvalidOperationException($"Unsupported job type: {job.GetType().Name}")
    };

    // ── Decompose ─────────────────────────────────────────────────────────────

    /// <summary>Runs the multi-phase decompose pipeline.</summary>
    private async IAsyncEnumerable<PhaseEvent> RunDecomposeAsync(
        DecomposeJob job,
        [EnumeratorCancellation] CancellationToken ct)
    {
        DecomposeOptions options = job.Options;

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

            // Resolve per-phase executor — allows different engines per phase.
            OrchestratorType phaseOrchestrator = options.ResolveOrchestrator(phase.Number);
            ILlmExecutor executor = ResolveExecutor(phaseOrchestrator);

            // Expansion phases with StartItem support selective deletion — preserve
            // the discovery file and any completed items below the start index.
            if ((phase.IsExpansion || phase.IsSynthesis) && options.StartItem.GetValueOrDefault() > 0)
                outputWriter.DeletePhaseFilesFrom(options.OutputRoot, options.ProjectName,
                    phase.Number, options.StartItem!.Value);
            else
                outputWriter.DeletePhaseFiles(options.OutputRoot, options.ProjectName, phase.Number);

            if (phase.IsExpansion)
            {
                await foreach (PhaseEvent evt in RunDecomposeExpansionPhaseAsync(phase, context, options, executor, ct))
                    yield return evt;
            }
            else if (phase.IsSynthesis)
            {
                await foreach (PhaseEvent evt in RunDecomposeSynthesisPhaseAsync(phase, context, options, executor, ct))
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

        // Per-phase override takes precedence over the phase spec's Output Mode field.
        bool useAppendResults = options.ResolveAppendResults(phase.Number, phase.UseAppendResults);
        OrchestratorType phaseOrchestrator = options.ResolveOrchestrator(phase.Number);

        // AppendResults phases use the output directory as CWD so the model can browse
        // prior phase spec files with its file tools. Standard phases use the source path.
        string workingDir = useAppendResults
            ? outputWriter.GetProjectDirectory(options.OutputRoot, options.ProjectName)
            : context.SourcePath;

        // Agentic backends (Claude, Copilot) can write output directly to the target file.
        // Not used with AppendResults — the tool handles output instead.
        string? directWritePath = useAppendResults
            ? null
            : (SupportsDirectWrite(phaseOrchestrator)
                ? outputWriter.GetPath(options.OutputRoot, options.ProjectName, phase.OutputFilename)
                : null);

        LlmExecutionContext ctx = BuildDecomposeContext(phase, prompt, options, workingDir, directWritePath);

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
            // If the model wrote the file directly, verify it exists and use it.
            if (directWritePath is not null && File.Exists(directWritePath) &&
                new FileInfo(directWritePath).Length > 0)
            {
                outputPath = directWritePath;

                // Strip any model preamble that leaked into the direct-write file.
                string raw = await File.ReadAllTextAsync(directWritePath, ct);
                string stripped = OutputWriter.StripPreamble(raw);
                if (stripped.Length != raw.Length)
                    await File.WriteAllTextAsync(directWritePath, stripped, new System.Text.UTF8Encoding(false), ct);

                logger.LogInformation("Phase {N}: model wrote directly to {Path}", phase.Number, outputPath);
            }
            else
            {
                // Fall back to saving the text output (OpenAI/Ollama, or direct write failed).
                outputPath = await outputWriter.WriteAsync(
                    options.OutputRoot, options.ProjectName, phase.OutputFilename,
                    output!.TrimEnd(), ct);
            }

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
        int startItem = options.StartItem.GetValueOrDefault();
        string discoveryFilename = $"{phase.Number:D2}-00-discovery.json";
        string discoveryPath = outputWriter.GetPath(options.OutputRoot, options.ProjectName, discoveryFilename);

        string? discoveryOutput = null;
        TokenUsage discoveryTokens = TokenUsage.Zero;

        // If resuming with StartItem, try loading the saved discovery JSON from disk.
        if (startItem > 0 && File.Exists(discoveryPath))
        {
            yield return new LogLine(phase.Number, $"Phase {phase.Number}: loading saved discovery from {discoveryFilename}");
            discoveryOutput = await File.ReadAllTextAsync(discoveryPath, ct);
        }
        else
        {
            yield return new LogLine(phase.Number, $"Phase {phase.Number}: identifying groups...");

            // Step 1 — discovery
            string discoveryPrompt = promptBuilder.BuildExpansionDiscoveryPrompt(phase, context);
            LlmExecutionContext discoveryCtx = BuildDecomposeContext(phase, discoveryPrompt, options, context.SourcePath,
                phaseLabel: $"phase-{phase.Number:D2}-discovery");

            string? discoveryError;
            IReadOnlyList<string> discoveryLines;
            (discoveryOutput, discoveryTokens, discoveryLines, discoveryError) =
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

            // Save discovery JSON as 03-00-discovery.json so it can be reused on re-runs.
            bool discoverySaved = false;
            try
            {
                await outputWriter.WriteAsync(
                    options.OutputRoot, options.ProjectName, discoveryFilename,
                    discoveryOutput!.Trim(), ct);
                discoverySaved = true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Phase {N}: failed to save discovery file: {Msg}", phase.Number, ex.Message);
                // Non-fatal — we still have the output in memory.
            }

            if (discoverySaved)
                yield return new LogLine(phase.Number, $"Saved discovery to {discoveryFilename}");
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

        yield return new LogLine(phase.Number,
            startItem > 0
                ? $"Found {items!.Count} groups. Resuming from item {startItem}..."
                : $"Found {items!.Count} groups. Speccing each...");

        IReadOnlyDictionary<string, string> expansionBasePaths = context.SnapshotPriorPaths();

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            JsonElement item = items[i];
            string itemName = GetString(item, "groupName") ?? GetString(item, "name") ?? $"item-{i + 1}";
            int index = i + 1;

            // Skip items below StartItem — they already have output on disk.
            if (startItem > 0 && index < startItem)
            {
                // Re-register existing output paths so later phases see prior context.
                string existingFilename = ResolveExpansionFilename(phase, index, itemName, options.ProjectName, string.Empty);
                string existingPath = outputWriter.GetPath(options.OutputRoot, options.ProjectName, existingFilename);
                if (File.Exists(existingPath))
                    context.PriorOutputPaths[Path.GetFileName(existingPath)] = existingPath;
                continue;
            }

            yield return new ExpansionItemStarted(phase.Number, index, itemName);

            string filename = ResolveExpansionFilename(phase, index, itemName, options.ProjectName, string.Empty);

            // Agentic backends can write the expansion spec directly to the output file.
            string? itemDirectWritePath = SupportsDirectWrite(options.ResolveOrchestrator(phase.Number))
                ? outputWriter.GetPath(options.OutputRoot, options.ProjectName, filename)
                : null;

            string itemPrompt = promptBuilder.BuildExpansionItemPrompt(phase, context, item, expansionBasePaths);
            LlmExecutionContext itemCtx = BuildDecomposeContext(phase, itemPrompt, options, context.SourcePath, itemDirectWritePath,
                $"phase-{phase.Number:D2}-{index:D2}");

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

            string? itemOutputPath = null;
            string? itemSaveError = null;
            try
            {
                // If the model wrote the file directly, verify and use it.
                if (itemDirectWritePath is not null && File.Exists(itemDirectWritePath) &&
                    new FileInfo(itemDirectWritePath).Length > 0)
                {
                    itemOutputPath = itemDirectWritePath;

                    // Strip any model preamble that leaked into the direct-write file.
                    string raw = await File.ReadAllTextAsync(itemDirectWritePath, ct);
                    string stripped = OutputWriter.StripPreamble(raw);
                    if (stripped.Length != raw.Length)
                        await File.WriteAllTextAsync(itemDirectWritePath, stripped, new System.Text.UTF8Encoding(false), ct);
                }
                else
                {
                    itemOutputPath = await outputWriter.WriteAsync(
                        options.OutputRoot, options.ProjectName, filename, itemOutput!.TrimEnd(), ct);
                }
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

    // ── Synthesis ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a synthesis phase: loads the source phase's discovery JSON, processes each
    /// group's spec file individually (avoiding context exhaustion), then merges all
    /// partial outputs into a single canonical document.
    /// </summary>
    private async IAsyncEnumerable<PhaseEvent> RunDecomposeSynthesisPhaseAsync(
        PhaseSpec phase, RunContext context, DecomposeOptions options, ILlmExecutor executor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        int sourcePhase = phase.SynthesisSourcePhase
            ?? throw new InvalidOperationException($"Phase {phase.Number} has no SynthesisSourcePhase.");
        int startItem = options.StartItem.GetValueOrDefault();

        // Step 1 — Load the source phase's discovery JSON (already created by Phase 03).
        string discoveryFilename = $"{sourcePhase:D2}-00-discovery.json";
        string discoveryPath = outputWriter.GetPath(options.OutputRoot, options.ProjectName, discoveryFilename);

        if (!File.Exists(discoveryPath))
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number,
                $"Synthesis source discovery file not found: {discoveryFilename}. " +
                $"Phase {sourcePhase} must complete before Phase {phase.Number} can run.");
            yield break;
        }

        yield return new LogLine(phase.Number,
            $"Phase {phase.Number}: loading discovery from {discoveryFilename}");

        string discoveryJson = await File.ReadAllTextAsync(discoveryPath, ct);

        List<JsonElement>? items = null;
        string? parseError = null;
        try { items = ParseJsonArray(discoveryJson); }
        catch (Exception ex) { parseError = ex.Message; }

        if (parseError is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number,
                $"Discovery JSON parse failed: {parseError}");
            yield break;
        }

        yield return new LogLine(phase.Number,
            startItem > 0
                ? $"Found {items!.Count} groups. Resuming from item {startItem}..."
                : $"Found {items!.Count} groups. Processing each...");

        // Step 2 — Per-chunk processing: run chunk prompt against each source spec file.
        List<string> partialOutputPaths = new();
        TokenUsage totalTokens = TokenUsage.Zero;

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            JsonElement item = items[i];
            string itemName = GetString(item, "groupName") ?? GetString(item, "name") ?? $"item-{i + 1}";
            int index = i + 1;

            // Derive the source phase's output filename for this group (the 03-xx spec file).
            string sourceSpecFilename = ResolveExpansionFilename(
                new PhaseSpec { OutputPattern = $"codeMap/<project>/{sourcePhase:D2}-{{index:00}}-{{slug}}.md" },
                index, itemName, options.ProjectName, string.Empty);
            string sourceSpecPath = outputWriter.GetPath(options.OutputRoot, options.ProjectName, sourceSpecFilename);

            // Derive the synthesis output filename for this chunk (06-xx-slug.md).
            string chunkFilename = ResolveExpansionFilename(phase, index, itemName, options.ProjectName, string.Empty);
            string chunkPath = outputWriter.GetPath(options.OutputRoot, options.ProjectName, chunkFilename);

            // Resume support: skip items below StartItem that already exist on disk.
            if (startItem > 0 && index < startItem)
            {
                if (File.Exists(chunkPath))
                    partialOutputPaths.Add(chunkPath);
                continue;
            }

            // Verify the source spec file exists.
            if (!File.Exists(sourceSpecPath))
            {
                yield return new LogLine(phase.Number,
                    $"  Warning: source spec not found for group '{itemName}': {sourceSpecFilename} — skipping");
                continue;
            }

            yield return new ExpansionItemStarted(phase.Number, index, itemName);

            // The 03-xx spec content is injected directly into the prompt so the model
            // doesn't waste turns reading it. But the model still needs file tools to read
            // the original source code files referenced in the spec.
            string chunkPrompt = promptBuilder.BuildSynthesisChunkPrompt(phase, context, item, sourceSpecPath);

            // File tools ON with the original source path as CWD — the model can browse
            // the source repository to verify and expand on what's in the spec.
            LlmExecutionContext ctx = BuildDecomposeContext(phase, chunkPrompt, options, context.SourcePath,
                phaseLabel: $"phase-{phase.Number:D2}-{index:D2}");

            (string? chunkOutput, TokenUsage chunkTokens, IReadOnlyList<string> chunkLines, string? chunkError) =
                await RunLlmCallWithRetryAsync(ctx, executor, ct);

            foreach (string line in chunkLines)
                yield return new LogLine(phase.Number, line);

            if (chunkTokens.Total > 0)
            {
                totalTokens = new TokenUsage(
                    totalTokens.InputTokens + chunkTokens.InputTokens,
                    totalTokens.OutputTokens + chunkTokens.OutputTokens,
                    totalTokens.CostUsd.HasValue || chunkTokens.CostUsd.HasValue
                        ? (totalTokens.CostUsd ?? 0) + (chunkTokens.CostUsd ?? 0)
                        : null);
                yield return new LogLine(phase.Number,
                    $"  Group '{itemName}': {chunkTokens.Total:N0} tokens" +
                    (chunkTokens.CostUsd.HasValue ? $" (~${chunkTokens.CostUsd:F4})" : string.Empty));
            }

            if (chunkError is not null)
            {
                context.LastFailed = true;
                yield return new PhaseFailed(phase.Number, $"Group '{itemName}': {chunkError}");
                yield break;
            }

            // Save the partial output.
            string? savedChunkPath = null;
            string? saveError = null;
            try
            {
                savedChunkPath = await outputWriter.WriteAsync(
                    options.OutputRoot, options.ProjectName, chunkFilename,
                    chunkOutput!.TrimEnd(), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                saveError = ex.Message;
            }

            if (saveError is not null)
            {
                context.LastFailed = true;
                yield return new PhaseFailed(phase.Number, $"Group '{itemName}' save failed: {saveError}");
                yield break;
            }

            partialOutputPaths.Add(savedChunkPath!);
            yield return new ExpansionItemCompleted(phase.Number, index, savedChunkPath!, chunkTokens);
        }

        // Step 3 — Merge: combine all partial outputs into the final canonical document.
        if (partialOutputPaths.Count == 0)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, "No partial outputs produced — nothing to merge.");
            yield break;
        }

        yield return new LogLine(phase.Number,
            $"Phase {phase.Number}: merging {partialOutputPaths.Count} partial inventories...");

        string mergePrompt = promptBuilder.BuildSynthesisMergePrompt(phase, context, partialOutputPaths);
        string mergeFilename = phase.OutputFilename;

        // Merge is text-in/text-out (all partial content is in the prompt) but needs
        // WorkingDirectory for the stream diagnostic log.
        string mergeOutputDir = outputWriter.GetProjectDirectory(options.OutputRoot, options.ProjectName);
        LlmExecutionContext mergeCtx = BuildSynthesisMergeContext(phase, mergePrompt, options, mergeOutputDir);

        (string? mergeOutput, TokenUsage mergeTokens, IReadOnlyList<string> mergeLines, string? mergeError) =
            await RunLlmCallWithRetryAsync(mergeCtx, executor, ct);

        foreach (string line in mergeLines)
            yield return new LogLine(phase.Number, line);

        if (mergeTokens.Total > 0)
        {
            totalTokens = new TokenUsage(
                totalTokens.InputTokens + mergeTokens.InputTokens,
                totalTokens.OutputTokens + mergeTokens.OutputTokens,
                totalTokens.CostUsd.HasValue || mergeTokens.CostUsd.HasValue
                    ? (totalTokens.CostUsd ?? 0) + (mergeTokens.CostUsd ?? 0)
                    : null);
            yield return new LogLine(phase.Number,
                $"Phase {phase.Number} merge: {mergeTokens.Total:N0} tokens" +
                (mergeTokens.CostUsd.HasValue ? $" (~${mergeTokens.CostUsd:F4})" : string.Empty));
        }

        if (mergeError is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, mergeError);
            yield break;
        }

        // Save the final merged output.
        string? finalOutputPath = null;
        string? finalSaveError = null;
        try
        {
            finalOutputPath = await outputWriter.WriteAsync(
                options.OutputRoot, options.ProjectName, mergeFilename,
                mergeOutput!.TrimEnd(), ct);

            context.PriorOutputPaths[mergeFilename] = finalOutputPath;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            finalSaveError = ex.Message;
        }

        if (finalSaveError is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, finalSaveError);
            yield break;
        }

        logger.LogInformation("Phase {N}: synthesis complete → {Path}", phase.Number, finalOutputPath);
        yield return new PhaseCompleted(phase.Number, finalOutputPath, totalTokens);
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

    // ── Verify ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Iterates all .md files in the decomposition output directory and runs the verify
    /// prompt against each one, producing a per-document accuracy audit report.
    /// </summary>
    private async IAsyncEnumerable<PhaseEvent> RunVerifyAsync(
        VerifyJob job,
        [EnumeratorCancellation] CancellationToken ct)
    {
        VerifyOptions options = job.Options;

        string decompDir = outputWriter.GetProjectDirectory(options.OutputRoot, options.ProjectName);
        if (!Directory.Exists(decompDir))
        {
            yield return new PhaseFailed(0, $"Decomposition output directory not found: {decompDir}");
            yield break;
        }

        // Prepare the verify output directory.
        string verifyDir = Path.Combine(options.OutputRoot, "Output", "Verification", options.ProjectName);
        Directory.CreateDirectory(verifyDir);

        // ── SummaryOnly mode: skip per-document verification ──────────────────
        if (options.SummaryOnly)
        {
            yield return new LogLine(0, "SummaryOnly mode — skipping per-document verification.");

            // Discover existing JSON verify reports on disk.
            string[] existingReports = Directory.Exists(verifyDir)
                ? Directory.GetFiles(verifyDir, "verify-*.json")
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];

            if (existingReports.Length == 0)
            {
                yield return new PhaseFailed(0, "No existing verification reports found — cannot produce summary.");
                yield break;
            }

            // Populate document progress entries from existing reports.
            for (int i = 0; i < existingReports.Length; i++)
            {
                job.Documents.Add(new DocumentVerifyProgress
                {
                    Index = i,
                    DocumentName = Path.GetFileName(existingReports[i]),
                    Status = JobStatus.Completed,
                    OutputPath = existingReports[i]
                });
            }
            job.NotifyChanged();

            yield return new LogLine(0, $"Found {existingReports.Length} existing reports. Running rollup + remediation.");

            // Jump straight to rollup and remediation.
            await foreach (PhaseEvent evt in RunVerifyRollupAsync(job, existingReports.ToList(), verifyDir, decompDir, ct))
                yield return evt;

            yield break;
        }

        // ── Per-document verification ─────────────────────────────────────────

        // Discover all .md files in the decomposition output, sorted by name.
        string[] mdFiles = Directory.GetFiles(decompDir, "*.md")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Apply document filter if specified.
        if (options.DocumentFilter is { Count: > 0 })
        {
            HashSet<string> filter = new(options.DocumentFilter, StringComparer.OrdinalIgnoreCase);
            mdFiles = mdFiles.Where(f => filter.Contains(Path.GetFileName(f))).ToArray();
        }

        if (mdFiles.Length == 0)
        {
            yield return new PhaseFailed(0, "No .md files found in decomposition output directory.");
            yield break;
        }

        // Populate job progress entries.
        for (int i = 0; i < mdFiles.Length; i++)
        {
            job.Documents.Add(new DocumentVerifyProgress
            {
                Index = i,
                DocumentName = Path.GetFileName(mdFiles[i])
            });
        }
        job.NotifyChanged();

        yield return new LogLine(0, $"Found {mdFiles.Length} documents to verify in {decompDir}");

        // Load the verify prompt template and extract the code block.
        string verifyTemplate = promptTemplates.VerifyTemplate;
        string verifyPrompt = ExtractVerifyPrompt(verifyTemplate);

        ILlmExecutor executor = ResolveExecutor(options.Orchestrator);

        for (int i = 0; i < mdFiles.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            string mdPath = mdFiles[i];
            string docName = Path.GetFileName(mdPath);

            yield return new PhaseStarted(i, docName);
            job.DocumentStarted(i);
            job.AppendLog($"--- Verifying: {docName} ---");

            // Read the document content and inject it into the prompt.
            string? documentContent = null;
            string? readError = null;
            try
            {
                documentContent = await File.ReadAllTextAsync(mdPath, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                readError = ex.Message;
            }

            if (readError is not null)
            {
                job.DocumentFailed(i, readError);
                yield return new PhaseFailed(i, $"Failed to read {docName}: {readError}");
                continue;
            }

            string prompt = verifyPrompt
                .Replace("<path>", options.SourcePath)
                .Replace("<document>", docName);

            // Structure: JSON instruction FIRST → document content → audit instructions → reminder.
            // Models lose track of early instructions when a huge document is in the middle.
            // Sandwiching the document between format instructions forces compliance.
            prompt = "CRITICAL: Your ENTIRE response must be a single JSON object. " +
                     "Start with { and end with }. No markdown. No code fences. No preamble. " +
                     "No thinking aloud. No sign-off. ONLY the JSON object.\n\n" +
                     $"## Document Under Audit: {docName}\n\n" +
                     $"```markdown\n{documentContent}\n```\n\n---\n\n{prompt}" +
                     "\n\nFINAL REMINDER: Output ONLY a JSON object. Start with { and end with }. " +
                     "Any non-JSON text in your response will cause a parse failure.";

            if (!string.IsNullOrWhiteSpace(options.Hints))
                prompt += $"\n\n---\n\n### Additional Audit Focus\n\n{options.Hints.Trim()}";

            LlmExecutionContext ctx = BuildVerifyContext(prompt, options);

            (string? output, TokenUsage tokens, IReadOnlyList<string> lines, string? error) =
                await RunLlmCallWithRetryAsync(ctx, executor, ct);

            foreach (string line in lines)
                yield return new LogLine(i, line);

            if (tokens.Total > 0)
                yield return new LogLine(i,
                    $"Verify {docName}: {tokens.Total:N0} tokens" +
                    (tokens.CostUsd.HasValue ? $" (~${tokens.CostUsd:F4})" : string.Empty));

            if (error is not null)
            {
                job.DocumentFailed(i, error);
                job.AppendLog($"Verify {docName} FAILED: {error}");
                yield return new PhaseFailed(i, error);
                continue; // Don't abort the whole run — keep verifying remaining documents.
            }

            // Save the verification report as JSON only. Render to markdown on-demand in the UI.
            string? outputPath = null;
            string? saveError = null;
            string? jsonParseWarning = null;
            try
            {
                string jsonBaseName = $"verify-{Path.GetFileNameWithoutExtension(docName)}.json";
                string jsonPath = Path.Combine(verifyDir, jsonBaseName);

                string raw = output!.TrimEnd();

                // Extract JSON from the LLM output — strip code fences or preamble.
                VerifyReport? report = ParseVerifyJson(raw, out string? parseError);

                if (report is null)
                {
                    // JSON parse failed — save raw output as .md fallback so nothing is lost.
                    string mdFallback = $"verify-{docName}";
                    outputPath = Path.Combine(verifyDir, mdFallback);
                    string fallback = OutputWriter.StripPreamble(raw);
                    await File.WriteAllTextAsync(outputPath, fallback,
                        new System.Text.UTF8Encoding(false), ct);
                    job.AppendLog($"Warning: JSON parse failed for {docName}: {parseError}");
                    jsonParseWarning = $"Warning: JSON parse failed — saved raw output as markdown. {parseError}";
                }
                else
                {
                    // Recompute header from actual claims — the model cannot be trusted to count.
                    report.Document = docName;
                    report.RecomputeHeader();

                    // Save structured JSON (source of truth for everything).
                    outputPath = jsonPath;
                    string prettyJson = JsonSerializer.Serialize(report, _verifyJsonOptions);
                    await File.WriteAllTextAsync(jsonPath, prettyJson,
                        new System.Text.UTF8Encoding(false), ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                saveError = ex.Message;
            }

            if (jsonParseWarning is not null)
                yield return new LogLine(i, jsonParseWarning);

            if (saveError is not null)
            {
                job.DocumentFailed(i, $"Save failed: {saveError}");
                yield return new PhaseFailed(i, saveError);
                continue;
            }

            string preview = output!.Length > 500 ? output[..500] + "…" : output;
            job.DocumentCompleted(i, outputPath, preview, tokens);
            job.AppendLog($"Verify {docName} completed → {Path.GetFileName(outputPath!)}");
            yield return new PhaseCompleted(i, outputPath, tokens);
        }

        // ── Rollup + Remediation ──────────────────────────────────────────────

        // Collect all completed verification JSON reports (including pre-existing ones
        // on disk when DocumentFilter was used for a partial re-run).
        List<string> reportPaths;
        if (options.DocumentFilter is { Count: > 0 })
        {
            // When filtering, include all JSON reports on disk (not just this run's).
            reportPaths = Directory.Exists(verifyDir)
                ? Directory.GetFiles(verifyDir, "verify-*.json")
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
        }
        else
        {
            // Use output paths from this run's completed documents. On success these are
            // .json paths; on JSON-parse failure they are .md fallbacks (skip those).
            reportPaths = job.Documents
                .Where(d => d.Status == JobStatus.Completed && d.OutputPath is not null
                         && d.OutputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.OutputPath!)
                .Where(File.Exists)
                .ToList();
        }

        await foreach (PhaseEvent evt in RunVerifyRollupAsync(job, reportPaths, verifyDir, decompDir, ct))
            yield return evt;
    }

    /// <summary>
    /// Runs the rollup summary and deterministic remediation. Loads structured JSON
    /// reports, builds a scorecard deterministically, calls LLM only for prose analysis,
    /// and aggregates all fixes without any LLM involvement.
    /// </summary>
    private async IAsyncEnumerable<PhaseEvent> RunVerifyRollupAsync(
        VerifyJob job,
        List<string> jsonReportPaths,
        string verifyDir,
        string decompDir,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (jsonReportPaths.Count == 0)
        {
            yield return new LogLine(job.Documents.Count,
                "No JSON verification reports found — skipping rollup. Re-run per-document verification.");
            yield break;
        }

        // ── Load and deserialize all JSON reports ─────────────────────────────

        List<VerifyReport> reports = [];
        List<string> loadWarnings = [];

        foreach (string jp in jsonReportPaths)
        {
            try
            {
                string json = await File.ReadAllTextAsync(jp, ct);
                VerifyReport? report = JsonSerializer.Deserialize<VerifyReport>(json, _verifyJsonOptions);
                if (report is not null)
                {
                    report.RecomputeHeader();
                    reports.Add(report);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                loadWarnings.Add($"Warning: could not load {Path.GetFileName(jp)}: {ex.Message}");
            }
        }

        foreach (string warning in loadWarnings)
            yield return new LogLine(job.Documents.Count, warning);

        if (reports.Count == 0)
        {
            yield return new LogLine(job.Documents.Count,
                "No valid JSON reports could be loaded — skipping rollup.");
            yield break;
        }

        // ── Step 1: Deterministic summary scorecard (NO LLM) ─────────────────

        int rollupIndex = job.Documents.Count;

        job.Documents.Add(new DocumentVerifyProgress
        {
            Index = rollupIndex,
            DocumentName = "PROJECT SUMMARY"
        });
        job.NotifyChanged();

        yield return new PhaseStarted(rollupIndex, "Project Summary");
        job.DocumentStarted(rollupIndex);
        job.AppendLog($"--- Summary: {reports.Count} reports loaded ---");

        // Build the deterministic scorecard from structured data.
        string scorecard = VerifyReportRenderer.RenderSummaryScorecard(reports);

        // Build a compact assessment digest for the LLM to summarize.
        StringBuilder assessmentDigest = new();
        foreach (VerifyReport r in reports.OrderBy(r => r.Document, StringComparer.OrdinalIgnoreCase))
        {
            assessmentDigest.AppendLine($"### {r.Document}");
            assessmentDigest.AppendLine($"- Claims: {r.Header.ClaimsAudited}, Passed: {r.Header.Passed}, " +
                $"Minor: {r.Header.Minor}, Major: {r.Header.Major}, Fabricated: {r.Header.Fabricated}, " +
                $"Accuracy: {r.Header.AccuracyPercent:F1}%");
            assessmentDigest.AppendLine($"- Assessment: {r.Header.OverallAssessment}");
            assessmentDigest.AppendLine();
        }

        // LLM summarizes the per-document assessments into cohesive prose.
        string summaryPrompt =
            "You are summarizing the results of a per-document verification audit.\n\n" +
            "Below are the per-document assessment results. Write a concise executive summary " +
            "(3-5 paragraphs) that synthesizes these into an overall quality assessment. " +
            "Identify which documents are strongest, which are weakest, and what the most " +
            "concerning patterns are. Be direct and specific — name documents and issues.\n\n" +
            "Do NOT produce tables, scorecards, or bullet lists of counts. The scorecard tables " +
            "will be appended below your prose automatically. Focus on INSIGHT, not numbers.\n\n" +
            "OUTPUT RULE: Respond with valid Markdown prose only. Start with a ## heading. " +
            "No preamble. No sign-off.\n\n" +
            $"---\n\n{assessmentDigest}";

        VerifyOptions options = job.Options;
        ILlmExecutor executor = ResolveExecutor(options.Orchestrator);

        LlmExecutionContext summaryCtx = new()
        {
            SystemPrompt    = promptTemplates.SystemPrompt,
            UserPrompt      = summaryPrompt,
            Model           = options.Model ?? string.Empty,
            ApiKey          = options.OpenAiApiKey,
            Endpoint        = options.OpenAiEndpoint,
            MaxOutputTokens = 4096,
            Timeout         = TimeSpan.FromMinutes(options.TimeoutMinutes),
            EnableFileTools = false,
            LogDirectory    = ResolveLogDirectory(options.OutputRoot, options.ProjectName),
            PhaseLabel      = "verify-summary"
        };

        (string? summaryProse, TokenUsage summaryTokens, IReadOnlyList<string> summaryLines, string? summaryError) =
            await RunLlmCallWithRetryAsync(summaryCtx, executor, ct);

        foreach (string line in summaryLines)
            yield return new LogLine(rollupIndex, line);

        if (summaryTokens.Total > 0)
            yield return new LogLine(rollupIndex,
                $"Summary: {summaryTokens.Total:N0} tokens" +
                (summaryTokens.CostUsd.HasValue ? $" (~${summaryTokens.CostUsd:F4})" : string.Empty));

        // Combine: LLM prose first, then deterministic scorecard tables below.
        string summaryContent = summaryError is not null
            ? scorecard
            : $"{OutputWriter.StripPreamble(summaryProse!.TrimEnd())}\n\n---\n\n{scorecard}";

        string? rollupPath = null;
        string? rollupSaveError = null;
        try
        {
            rollupPath = Path.Combine(verifyDir, "verify-summary.md");
            await File.WriteAllTextAsync(rollupPath, summaryContent,
                new System.Text.UTF8Encoding(false), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            rollupSaveError = ex.Message;
        }

        if (summaryError is not null)
        {
            job.AppendLog($"Summary LLM call failed: {summaryError} — saved scorecard only.");
            yield return new LogLine(rollupIndex, $"Warning: LLM summary failed — scorecard saved without prose. {summaryError}");
        }

        if (rollupSaveError is not null)
        {
            job.DocumentFailed(rollupIndex, $"Save failed: {rollupSaveError}");
            yield return new PhaseFailed(rollupIndex, rollupSaveError);
        }
        else
        {
            string rollupPreview = summaryProse is not null && summaryProse.Length > 500
                ? summaryProse[..500] + "\u2026" : (summaryProse ?? "Scorecard only");
            job.DocumentCompleted(rollupIndex, rollupPath, rollupPreview, summaryTokens);
            job.AppendLog($"Summary completed \u2192 verify-summary.md");
            yield return new PhaseCompleted(rollupIndex, rollupPath, summaryTokens);
        }

        // ── Step 2: Deterministic remediation (NO LLM) ───────────────────────

        int remediateIndex = job.Documents.Count;

        job.Documents.Add(new DocumentVerifyProgress
        {
            Index = remediateIndex,
            DocumentName = "REMEDIATION PLAN"
        });
        job.NotifyChanged();

        yield return new PhaseStarted(remediateIndex, "Remediation Plan");
        job.DocumentStarted(remediateIndex);

        int totalFixes = reports
            .SelectMany(r => r.Claims)
            .Count(c => c is { Status: "FAIL", Fix: not null });

        job.AppendLog($"--- Remediation: aggregating {totalFixes} fixes from {reports.Count} reports (deterministic) ---");

        string? remediatePath = null;
        string? remediateSaveError = null;
        try
        {
            remediatePath = Path.Combine(verifyDir, "verify-remediate.md");

            string remediateContent = VerifyReportRenderer.RenderRemediation(reports);

            await File.WriteAllTextAsync(remediatePath, remediateContent,
                new System.Text.UTF8Encoding(false), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            remediateSaveError = ex.Message;
        }

        if (remediateSaveError is not null)
        {
            job.DocumentFailed(remediateIndex, $"Save failed: {remediateSaveError}");
            yield return new PhaseFailed(remediateIndex, remediateSaveError);
        }
        else
        {
            string remediatePreview = $"{totalFixes} fixes aggregated deterministically from {reports.Count} reports.";
            job.DocumentCompleted(remediateIndex, remediatePath, remediatePreview, TokenUsage.Zero);
            job.AppendLog($"Remediation completed → verify-remediate.md ({totalFixes} fixes, 0 tokens — deterministic)");
            yield return new PhaseCompleted(remediateIndex, remediatePath, TokenUsage.Zero);
        }
    }

    /// <summary>
    /// Extracts the per-document audit prompt from verify.md — the first code block
    /// (under the ## Verify section).
    /// </summary>
    private static string ExtractVerifyPrompt(string template)
    {
        // The first code block is the per-document prompt.
        Match fence = Regex.Match(template, @"```\s*\n([\s\S]*?)```", RegexOptions.Multiline);
        if (fence.Success)
            return fence.Groups[1].Value.Trim();

        // Fallback: return everything after "## Verify" heading.
        int idx = template.IndexOf("## Verify", StringComparison.Ordinal);
        return idx >= 0 ? template[(idx + "## Verify".Length)..].Trim() : template.Trim();
    }

    /// <summary>
    /// Extracts the rollup/summary prompt from verify.md — the second code block
    /// (under the ## Rollup section).
    /// </summary>
    private static string ExtractRollupPrompt(string template)
    {
        // Find all code blocks — the second one is the rollup prompt.
        MatchCollection fences = Regex.Matches(template, @"```\s*\n([\s\S]*?)```", RegexOptions.Multiline);
        if (fences.Count >= 2)
            return fences[1].Groups[1].Value.Trim();

        // Fallback: return everything after "## Rollup" heading.
        int idx = template.IndexOf("## Rollup", StringComparison.Ordinal);
        return idx >= 0 ? template[(idx + "## Rollup".Length)..].Trim() : template.Trim();
    }

    /// <summary>
    /// Extracts the remediation prompt from verify.md — the third code block.
    /// Retained for backward compatibility but no longer called in the deterministic pipeline.
    /// </summary>
    private static string ExtractRemediationPrompt(string template)
    {
        MatchCollection fences = Regex.Matches(template, @"```\s*\n([\s\S]*?)```", RegexOptions.Multiline);
        if (fences.Count >= 3)
            return fences[2].Groups[1].Value.Trim();

        int idx = template.IndexOf("## Remediation", StringComparison.Ordinal);
        return idx >= 0 ? template[(idx + "## Remediation".Length)..].Trim() : template.Trim();
    }

    /// <summary>
    /// Parses JSON from LLM output into a <see cref="VerifyReport"/>. Handles
    /// common LLM quirks: markdown code fences, preamble text before the JSON,
    /// trailing content after the closing brace, and prose scattered around the
    /// actual JSON object.
    /// </summary>
    private static VerifyReport? ParseVerifyJson(string raw, out string? error)
    {
        error = null;
        string text = raw.Trim();

        // Strategy 1: If the output is wrapped in a markdown code fence, extract the fence content.
        Match fenceMatch = Regex.Match(text, @"```(?:json)?\s*\n([\s\S]*?)```", RegexOptions.Multiline);
        if (fenceMatch.Success)
            text = fenceMatch.Groups[1].Value.Trim();

        // Strategy 2: Look for the JSON object that matches our schema by searching for
        // known root-level keys. This avoids false matches on stray braces in prose.
        string json = FindJsonObject(text);

        if (json.Length == 0)
        {
            // Strategy 3: Fallback — simple first-{ to last-} extraction.
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                error = "No JSON object found in LLM output.";
                return null;
            }
            json = text[start..(end + 1)];
        }

        try
        {
            return JsonSerializer.Deserialize<VerifyReport>(json, _verifyJsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"JSON deserialization failed: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Finds a JSON object in mixed text by locating a <c>{</c> that is followed (within the
    /// object) by one of our schema's root keys (<c>"document"</c>, <c>"header"</c>, <c>"claims"</c>),
    /// then tracks brace depth to find the matching <c>}</c>. Returns the substring, or empty
    /// string if no match is found.
    /// </summary>
    private static string FindJsonObject(string text)
    {
        // Scan for each '{' and check if the next ~200 chars contain a known root key.
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;

            // Peek ahead for a root-level key within a reasonable window.
            int peekEnd = Math.Min(i + 200, text.Length);
            ReadOnlySpan<char> peek = text.AsSpan(i, peekEnd - i);
            if (!peek.Contains("\"document\"", StringComparison.Ordinal) &&
                !peek.Contains("\"header\"", StringComparison.Ordinal) &&
                !peek.Contains("\"claims\"", StringComparison.Ordinal))
                continue;

            // Brace-depth tracking to find the matching close brace.
            // Respects JSON string literals to avoid counting braces inside strings.
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int j = i; j < text.Length; j++)
            {
                char c = text[j];

                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text[i..(j + 1)];
                }
            }

            // Unbalanced braces — fall through to simple fallback.
            break;
        }

        return string.Empty;
    }

    /// <summary>
    /// Builds the LLM execution context for a verify call. The model needs file-reading tools
    /// to browse the source repository and verify claims against the actual code.
    /// </summary>
    private LlmExecutionContext BuildVerifyContext(string prompt, VerifyOptions options) =>
        new()
        {
            SystemPrompt            = promptTemplates.SystemPrompt,
            UserPrompt              = prompt,
            Model                   = options.Model ?? string.Empty,
            ApiKey                  = options.OpenAiApiKey,
            Endpoint                = options.OpenAiEndpoint,
            WorkingDirectory        = options.SourcePath,
            MaxTurns                = options.MaxTurns,
            MaxOutputTokens         = options.MaxOutputTokens,
            Timeout                 = TimeSpan.FromMinutes(options.TimeoutMinutes),
            EnableReadOnlyFileTools = true,
            EnableFileTools         = true,
            IgnoreContent           = promptTemplates.TransmuteIgnore,
            FileToolsRoot           = options.SourcePath,
            LogDirectory            = ResolveLogDirectory(options.OutputRoot, options.ProjectName),
            PhaseLabel              = "verify",
            JsonOutputMode          = true
        };

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
        PhaseSpec phase, string prompt, DecomposeOptions options, string sourcePath,
        string? outputFilePath = null, string? phaseLabel = null)
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
            SystemPrompt            = promptTemplates.SystemPrompt,
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
            FileToolsRoot = sourcePath,
            OutputFilePath = outputFilePath,
            EnableAppendResultsTool = options.ResolveAppendResults(phase.Number, phase.UseAppendResults),
            LogDirectory = ResolveLogDirectory(options.OutputRoot, options.ProjectName),
            PhaseLabel   = phaseLabel ?? $"phase-{phase.Number:D2}"
        };
    }

    private LlmExecutionContext BuildComposeContext(string prompt, ComposeOptions options) =>
        new()
        {
            SystemPrompt    = promptTemplates.SystemPrompt,
            UserPrompt      = prompt,
            Model           = options.Model,
            ApiKey          = options.OpenAiApiKey,
            Endpoint        = options.OpenAiEndpoint,
            MaxOutputTokens = options.MaxOutputTokens,
            Timeout         = TimeSpan.FromMinutes(options.TimeoutMinutes),
            EnableFileTools = false,
            LogDirectory    = ResolveLogDirectory(options.OutputRoot, options.OutputName),
            PhaseLabel      = "compose"
        };

    /// <summary>
    /// Builds the context for a synthesis merge call. No file tools — all partial
    /// content is in the prompt. <paramref name="workingDirectory"/> is set solely
    /// to enable the stream diagnostic log.
    /// </summary>
    private LlmExecutionContext BuildSynthesisMergeContext(
        PhaseSpec phase, string prompt, DecomposeOptions options, string workingDirectory)
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

        return new LlmExecutionContext
        {
            SystemPrompt     = promptTemplates.SystemPrompt,
            UserPrompt       = prompt,
            Model            = ResolveModel(phase.ModelWeight, options),
            ApiKey           = options.OpenAiApiKey,
            Endpoint         = options.OpenAiEndpoint,
            MaxOutputTokens  = maxTokens,
            Timeout          = TimeSpan.FromMinutes(options.TimeoutMinutes),
            WorkingDirectory = workingDirectory,
            EnableFileTools  = false,
            LogDirectory     = ResolveLogDirectory(options.OutputRoot, options.ProjectName),
            PhaseLabel       = $"phase-{phase.Number:D2}-merge"
        };
    }

    private LlmExecutionContext BuildImplementContext(
        string claudePrompt, string planContent, ImplementOptions options, string outputDir) =>
        new()
        {
            SystemPrompt     = promptTemplates.SystemPrompt,
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
            IgnoreContent    = promptTemplates.TransmuteIgnore,
            LogDirectory     = Path.Combine(outputDir, "_logs"),
            PhaseLabel       = "implement"
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ILlmExecutor ResolveExecutor(OrchestratorType type) =>
        executors.FirstOrDefault(e => e.BackendType == type)
            ?? throw new InvalidOperationException($"No ILlmExecutor registered for {type}.");

    /// <summary>
    /// Returns true for backends with native file-write tools (Claude CLI, Copilot SDK).
    /// These can write output files directly instead of returning text on stdout.
    /// </summary>
    private static bool SupportsDirectWrite(OrchestratorType type) =>
        type is OrchestratorType.ClaudeCode;
        // CopilotCli excluded — its file-write tool routes through shell permission,
        // which we block. The model produces text output instead; the orchestrator saves it.

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

    /// <summary>
    /// Returns the log directory for a given output root and project name:
    /// <c>{outputRoot}/Output/Logs/{projectName}/</c>.
    /// </summary>
    private static string ResolveLogDirectory(string outputRoot, string projectName)
        => Path.Combine(outputRoot, "Output", "Logs", projectName);

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
