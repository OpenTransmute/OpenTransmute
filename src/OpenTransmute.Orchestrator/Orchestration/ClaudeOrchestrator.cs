using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenTransmute.Orchestrator.Contracts;
using OpenTransmute.Orchestrator.Model;
using OpenTransmute.Orchestrator.Output;
using OpenTransmute.Orchestrator.Parsing;

namespace OpenTransmute.Orchestrator.Orchestration;

/// <summary>
/// Drives the decompose pipeline by invoking the claude CLI as a subprocess for each phase.
/// Phase 3 is an expansion phase driven by its two code blocks from decompose.md.
/// Each stdout line is yielded as a LogLine event for real-time UI streaming.
/// </summary>
public sealed class ClaudeOrchestrator(
    PromptTemplates templates,
    PromptBuilder builder,
    OutputWriter outputWriter,
    ILogger<ClaudeOrchestrator> logger) : IDecomposeOrchestrator
{
    #region Members

    // Default Claude model names per weight tier
    private const string OpusModel   = "claude-opus-4-6";
    private const string SonnetModel = "claude-sonnet-4-6";
    private const string HaikuModel  = "claude-haiku-4-5-20251001";

    private static readonly Regex FenceRegex =
        new(@"^```[^\n]*\n([\s\S]*?)```\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    #endregion

    #region Properties

    public OrchestratorType Type => OrchestratorType.ClaudeCode;

    #endregion

    #region Methods

    public async IAsyncEnumerable<PhaseEvent> RunAsync(
        DecomposeRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        RunContext context = new RunContext(request);

        if (request.StartPhase > 0)
            context.LoadPriorOutputs(request.StartPhase);

        foreach (PhaseSpec phase in templates.Phases.Where(p =>
            p.Number >= request.StartPhase &&
            (request.EndPhase == null || p.Number <= request.EndPhase)))
        {
            yield return new PhaseStarted(phase.Number, phase.Title);
            outputWriter.DeletePhaseFiles(request.OutputRoot, request.ProjectName, phase.Number);

            if (phase.IsExpansion)
            {
                await foreach (PhaseEvent evt in RunExpansionPhaseAsync(phase, context, request, ct))
                    yield return evt;
            }
            else
            {
                await foreach (PhaseEvent evt in RunSimplePhaseAsync(phase, context, request, ct))
                    yield return evt;
            }

            if (context.LastFailed) yield break;
        }
    }

    // ── Simple phase ──────────────────────────────────────────────────────────

    private async IAsyncEnumerable<PhaseEvent> RunSimplePhaseAsync(
        PhaseSpec phase, RunContext context, DecomposeRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string prompt = builder.BuildSimplePrompt(phase, context);
        logger.LogInformation("Phase {N} ({Title}): invoking claude", phase.Number, phase.Title);

        // Stream subprocess output — yield LogLine events as they arrive.
        // Error detection comes via the isError flag, not exceptions, so no try/catch here.
        StringBuilder outputBuilder = new StringBuilder();
        string? errorMessage = null;

        await foreach ((bool isError, string text) in InvokeClaudeAsync(phase.Number, request, phase, prompt, ct))
        {
            if (isError) { errorMessage = text; break; }
            outputBuilder.AppendLine(text);
            yield return new LogLine(phase.Number, text);
        }

        // File save — try/catch outside any yield
        string? outputPath = null;
        if (errorMessage is null)
        {
            string? saveError = null;
            try
            {
                outputPath = await outputWriter.WriteAsync(
                    request.OutputRoot, request.ProjectName, phase.OutputFilename,
                    outputBuilder.ToString().TrimEnd(), ct);
                context.PriorOutputPaths[phase.OutputFilename] = outputPath;
                logger.LogInformation("Phase {N}: saved {Path}", phase.Number, outputPath);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                saveError = ex.Message;
            }

            if (saveError is not null) errorMessage = saveError;
        }

        if (errorMessage is not null)
        {
            logger.LogError("Phase {N} failed: {Err}", phase.Number, errorMessage);
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, errorMessage);
        }
        else
        {
            yield return new PhaseCompleted(phase.Number, outputPath, TokenUsage.Zero);
        }
    }

    // ── Expansion phase (Phase 3) ─────────────────────────────────────────────

    private async IAsyncEnumerable<PhaseEvent> RunExpansionPhaseAsync(
        PhaseSpec phase, RunContext context, DecomposeRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new LogLine(phase.Number, $"Phase {phase.Number}: identifying groups...");

        // Step 1 — discovery
        string discoveryPrompt  = builder.BuildExpansionDiscoveryPrompt(phase, context);
        StringBuilder discoveryBuilder = new StringBuilder();
        string? errorMessage = null;

        await foreach ((bool isError, string text) in InvokeClaudeAsync(phase.Number, request, phase, discoveryPrompt, ct))
        {
            if (isError) { errorMessage = text; break; }
            discoveryBuilder.AppendLine(text);
            yield return new LogLine(phase.Number, text);
        }

        if (errorMessage is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, errorMessage);
            yield break;
        }

        // Discovery JSON is parsed in memory; no separate file needed

        // Parse item list — failure yields PhaseFailed, no exception propagation
        List<JsonElement>? items = null;
        string? parseError = null;
        try
        {
            items = ParseJsonArray(discoveryBuilder.ToString());
        }
        catch (Exception ex)
        {
            parseError = ex.Message;
        }

        if (parseError is not null)
        {
            logger.LogError("Phase {N}: discovery JSON parse failed: {Err}", phase.Number, parseError);
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, $"Discovery JSON parse failed: {parseError}");
            yield break;
        }

        yield return new LogLine(phase.Number, $"Found {items!.Count} groups. Speccing each...");

        // Snapshot prior paths before the loop so all items get the same baseline context
        // and do not accumulate each other's outputs as they complete.
        IReadOnlyDictionary<string, string> expansionBasePaths = context.SnapshotPriorPaths();

        // Step 2 — per-item spec
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            JsonElement item = items[i];
            string itemName  = GetString(item, "groupName") ?? GetString(item, "name") ?? $"item-{i + 1}";
            int index        = i + 1;

            yield return new ExpansionItemStarted(phase.Number, index, itemName);

            string itemPrompt  = builder.BuildExpansionItemPrompt(phase, context, item, expansionBasePaths);
            StringBuilder itemBuilder = new StringBuilder();
            string? itemError = null;

            await foreach ((bool isError, string text) in InvokeClaudeAsync(phase.Number, request, phase, itemPrompt, ct))
            {
                if (isError) { itemError = text; break; }
                itemBuilder.AppendLine(text);
                yield return new LogLine(phase.Number, text);
            }

            if (itemError is not null)
            {
                context.LastFailed = true;
                yield return new PhaseFailed(phase.Number, $"Group '{itemName}': {itemError}");
                yield break;
            }

            string? itemOutputPath = null;
            string? itemSaveError  = null;
            try
            {
                string filename = ResolveExpansionFilename(phase, index, itemName, request);
                itemOutputPath = await outputWriter.WriteAsync(
                    request.OutputRoot, request.ProjectName,
                    filename, itemBuilder.ToString().TrimEnd(), ct);
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

            yield return new ExpansionItemCompleted(phase.Number, index, itemOutputPath!, TokenUsage.Zero);
        }

        yield return new PhaseCompleted(phase.Number, null, TokenUsage.Zero);
    }

    // ── Subprocess ────────────────────────────────────────────────────────────

    /// <summary>
    /// Launches the claude CLI and yields (isError=false, line) for each stdout line.
    /// On non-zero exit, yields a single (isError=true, errorDescription) sentinel.
    /// </summary>
    private async IAsyncEnumerable<(bool isError, string text)> InvokeClaudeAsync(
        int phaseNumber,
        DecomposeRequest request,
        PhaseSpec phase,
        string prompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ProcessStartInfo psi = BuildProcessStartInfo(request, phase, prompt);
        using Process process = new Process { StartInfo = psi };

        StringBuilder errorsBuilder = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) errorsBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        while (await process.StandardOutput.ReadLineAsync(ct) is string line)
            yield return (false, line);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string err = errorsBuilder.ToString().Trim();
            logger.LogError("claude exited {Code} phase {N}: {Err}", process.ExitCode, phaseNumber, err);

            string message = err.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || err.Contains("429")
                ? $"Rate limit hit — {err}"
                : $"claude exited {process.ExitCode}: {(err.Length > 0 ? err : "no stderr")}";

            yield return (true, message);
        }
    }

    private ProcessStartInfo BuildProcessStartInfo(DecomposeRequest request, PhaseSpec phase, string prompt)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName               = "claude",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = request.SourcePath,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        string systemPrompt = templates.SystemPrompt
            + $"\n\nYou are executing Phase {phase.Number}: {phase.Title}.\nGoal: {phase.Goal}\nProject: {request.ProjectName}"
            + "\n\nOutput your complete response as plain text to stdout. "
            + "Do NOT use file-write or file-edit tools — the orchestrator captures your stdout and handles all file saving."
            + (string.IsNullOrWhiteSpace(request.Hints)
                ? string.Empty
                : "\n\n## Analyst Hints\nThe user has provided the following domain knowledge. Treat these as authoritative context throughout every phase:\n\n" + request.Hints.Trim());

        psi.ArgumentList.Add("--system-prompt");
        psi.ArgumentList.Add(systemPrompt);
        psi.ArgumentList.Add("--dangerously-skip-permissions");
        psi.ArgumentList.Add("--disable-slash-commands");
        psi.ArgumentList.Add("--max-turns");
        psi.ArgumentList.Add(request.MaxTurns.ToString());
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("text");

        // Select the Claude model that matches the phase weight
        string model = ResolveClaudeModel(phase.ModelWeight);
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(model);

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);

        return psi;
    }

    private static string ResolveClaudeModel(string modelWeight) => modelWeight switch
    {
        "thick" => OpusModel,
        "thin"  => HaikuModel,
        _       => SonnetModel
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<JsonElement> ParseJsonArray(string raw)
    {
        string json = raw.Trim();
        Match m = FenceRegex.Match(json);
        if (m.Success) json = m.Groups[1].Value.Trim();

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Discovery response is not a JSON array.");

        return doc.RootElement.EnumerateArray()
            .Select(e => JsonDocument.Parse(e.GetRawText()).RootElement.Clone())
            .ToList();
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
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(input.Select(c => invalid.Contains(c) ? '-' : char.ToLowerInvariant(c)).ToArray());
        return string.Join("-", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    }

    private static string ResolveExpansionFilename(
        PhaseSpec phase, int index, string itemName, DecomposeRequest request)
    {
        string pattern = phase.OutputPattern
            ?? throw new InvalidOperationException($"Phase {phase.Number} has no OutputPattern.");

        string full = pattern
            .Replace("{slug}",     Slug(itemName))
            .Replace("{index:00}", index.ToString("00"))
            .Replace("<project>",  request.ProjectName, StringComparison.OrdinalIgnoreCase)
            .Replace("<path>",     request.SourcePath,  StringComparison.OrdinalIgnoreCase);

        return Path.GetFileName(full);
    }

    #endregion
}
