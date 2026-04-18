using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenTransmute.Orchestrator.Contracts;
using OpenTransmute.Orchestrator.Exceptions;
using OpenTransmute.Orchestrator.Model;
using OpenTransmute.Orchestrator.Output;
using OpenTransmute.Orchestrator.Parsing;
using OpenTransmute.Orchestrator.Plugins;
using OpenTransmute.Orchestrator.Retry;

namespace OpenTransmute.Orchestrator.Orchestration;

/// <summary>
/// Drives the decompose pipeline via Microsoft.Extensions.AI and the OpenAI API (or Ollama).
/// A single IChatClient is built per run so the HTTP connection is reused.
/// Token usage is extracted from ChatCompletion.Usage and accumulated in RunContext.
/// </summary>
public sealed class OpenAiOrchestrator(
    PromptTemplates templates,
    PromptBuilder builder,
    OutputWriter outputWriter,
    RetryPolicy retryPolicy,
    ILogger<OpenAiOrchestrator> logger,
    OrchestratorType type = OrchestratorType.OpenAI) : IDecomposeOrchestrator
{
    #region Members

    private static readonly Regex FenceRegex =
        new(@"^```[^\n]*\n([\s\S]*?)```\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

    // Cached at startup — Path.GetInvalidFileNameChars() allocates each call.
    private static readonly HashSet<char> InvalidFileNameChars =
        new HashSet<char>(Path.GetInvalidFileNameChars());

    #endregion

    #region Properties

    public OrchestratorType Type => type;

    private bool IsOllama => type == OrchestratorType.Ollama;

    #endregion

    #region Methods

    public async IAsyncEnumerable<PhaseEvent> RunAsync(
        DecomposeRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (type == OrchestratorType.OpenAI && string.IsNullOrWhiteSpace(request.OpenAiApiKey))
            throw new OrchestratorException("OpenAiApiKey is required for the OpenAI orchestrator.");

        RunContext context = new RunContext(request);

        if (request.StartPhase > 0)
            context.LoadPriorOutputs(request.StartPhase);

        (IChatClient client, IList<AITool> tools) = BuildChatClient(request);

        foreach (PhaseSpec phase in templates.Phases.Where(p =>
            p.Number >= request.StartPhase &&
            (request.EndPhase == null || p.Number <= request.EndPhase)))
        {
            yield return new PhaseStarted(phase.Number, phase.Title);
            outputWriter.DeletePhaseFiles(request.OutputRoot, request.ProjectName, phase.Number);

            if (phase.IsExpansion)
            {
                await foreach (PhaseEvent evt in RunExpansionPhaseAsync(phase, context, request, client, tools, ct))
                    yield return evt;
            }
            else
            {
                await foreach (PhaseEvent evt in RunSimplePhaseAsync(phase, context, request, client, tools, ct))
                    yield return evt;
            }

            if (context.LastFailed) yield break;
        }
    }

    // ── Simple phase ──────────────────────────────────────────────────────────

    private async IAsyncEnumerable<PhaseEvent> RunSimplePhaseAsync(
        PhaseSpec phase, RunContext context, DecomposeRequest request,
        IChatClient client, IList<AITool> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string prompt = builder.BuildSimplePrompt(phase, context);
        string? errorMessage = null;
        TokenUsage tokens = TokenUsage.Zero;
        string? outputPath = null;
        string? logMessage = null;

        try
        {
            (string content, TokenUsage usage) = await retryPolicy.ExecuteAsync(
                innerCt => InvokeAsync(phase, prompt, request, client, tools, innerCt), ct);

            tokens = usage;
            context.Tokens.Add(tokens);
            logMessage = $"Phase {phase.Number}: {tokens.Total:N0} tokens"
                + (tokens.CostUsd.HasValue ? $" (~${tokens.CostUsd:F4})" : string.Empty);

            outputPath = await outputWriter.WriteAsync(
                request.OutputRoot, request.ProjectName, phase.OutputFilename, content, ct);
            context.PriorOutputPaths[phase.OutputFilename] = outputPath;
            logger.LogInformation("Phase {N}: saved {Path}", phase.Number, outputPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            logger.LogError(ex, "Phase {N} failed", phase.Number);
        }

        if (logMessage is not null) yield return new LogLine(phase.Number, logMessage);

        if (errorMessage is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, errorMessage);
        }
        else
        {
            yield return new PhaseCompleted(phase.Number, outputPath, tokens);
        }
    }

    // ── Expansion phase ───────────────────────────────────────────────────────

    private async IAsyncEnumerable<PhaseEvent> RunExpansionPhaseAsync(
        PhaseSpec phase, RunContext context, DecomposeRequest request,
        IChatClient client, IList<AITool> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Step 1 — discovery
        string discoveryJson = string.Empty;
        string? errorMessage = null;
        TokenUsage discoveryTokens = TokenUsage.Zero;
        string? discoveryLog = null;

        string discoveryPrompt = builder.BuildExpansionDiscoveryPrompt(phase, context);

        try
        {
            (string content, TokenUsage usage) = await retryPolicy.ExecuteAsync(
                innerCt => InvokeAsync(phase, discoveryPrompt, request, client, tools, innerCt), ct);

            discoveryJson   = content;
            discoveryTokens = usage;
            context.Tokens.Add(usage);
            discoveryLog = $"Phase {phase.Number} discovery: {usage.Total:N0} tokens"
                + (usage.CostUsd.HasValue ? $" (~${usage.CostUsd:F4})" : string.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            logger.LogError(ex, "Phase {N} expansion discovery failed", phase.Number);
        }

        if (discoveryLog is not null) yield return new LogLine(phase.Number, discoveryLog);

        if (errorMessage is not null)
        {
            context.LastFailed = true;
            yield return new PhaseFailed(phase.Number, errorMessage);
            yield break;
        }

        // Parse items — capture error without exception
        List<JsonElement>? items = null;
        string? parseError = null;
        try { items = ParseJsonArray(discoveryJson); }
        catch (Exception ex) { parseError = ex.Message; }

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

        // Step 2 — per-item specs
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            JsonElement item = items[i];
            string itemName  = GetString(item, "groupName") ?? GetString(item, "name") ?? $"item-{i + 1}";
            int index        = i + 1;

            yield return new ExpansionItemStarted(phase.Number, index, itemName);

            string? itemError = null;
            TokenUsage itemTokens = TokenUsage.Zero;
            string? itemOutputPath = null;
            string? itemLog = null;

            string itemPrompt = builder.BuildExpansionItemPrompt(phase, context, item, expansionBasePaths);

            try
            {
                (string content, TokenUsage usage) = await retryPolicy.ExecuteAsync(
                    innerCt => InvokeAsync(phase, itemPrompt, request, client, tools, innerCt), ct);

                itemTokens = usage;
                context.Tokens.Add(usage);
                itemLog = $"  Group '{itemName}': {usage.Total:N0} tokens"
                    + (usage.CostUsd.HasValue ? $" (~${usage.CostUsd:F4})" : string.Empty);

                string filename = ResolveExpansionFilename(phase, index, itemName, request);
                itemOutputPath = await outputWriter.WriteAsync(
                    request.OutputRoot, request.ProjectName, filename, content, ct);
                context.PriorOutputPaths[Path.GetFileName(itemOutputPath)] = itemOutputPath;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { itemError = ex.Message; }

            if (itemLog is not null) yield return new LogLine(phase.Number, itemLog);

            if (itemError is not null)
            {
                context.LastFailed = true;
                yield return new PhaseFailed(phase.Number, $"Group '{itemName}': {itemError}");
                yield break;
            }

            yield return new ExpansionItemCompleted(phase.Number, index, itemOutputPath!, itemTokens);
        }

        yield return new PhaseCompleted(phase.Number, null, discoveryTokens);
    }

    // ── MEA invocation ────────────────────────────────────────────────────────

    private async Task<(string Content, TokenUsage Tokens)> InvokeAsync(
        PhaseSpec phase, string prompt, DecomposeRequest request,
        IChatClient client, IList<AITool> tools, CancellationToken ct)
    {
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System, templates.SystemPrompt),
            new ChatMessage(ChatRole.System,
                $"You are executing Phase {phase.Number}: {phase.Title}.\nGoal: {phase.Goal}\nProject: {request.ProjectName}"),
            new ChatMessage(ChatRole.User, prompt),
        ];

        int weightDefault = phase.ModelWeight switch
        {
            "thick" => request.ThickMaxOutputTokens,
            "thin"  => request.ThinMaxOutputTokens,
            _       => request.RegularMaxOutputTokens
        };
        int maxTokens = request.MaxOutputTokens == 0
            ? weightDefault
            : Math.Min(request.MaxOutputTokens, weightDefault);

        ChatOptions options = new ChatOptions
        {
            Tools           = [.. tools],
            ToolMode        = ChatToolMode.Auto,
            MaxOutputTokens = maxTokens,
            ModelId         = ResolveModel(phase, request),
        };

        ChatResponse result;
        try
        {
            result = await client.GetResponseAsync(messages, options, ct);
        }
        catch (Exception ex) when (
            ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("429"))
        {
            throw new OrchestratorRateLimitException();
        }

        string? content = result.Text;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                $"LLM returned empty content for phase {phase.Number} ({phase.Title}).");

        return (content, ExtractTokenUsage(result));
    }

    private (IChatClient Client, IList<AITool> Tools) BuildChatClient(DecomposeRequest request)
    {
        string baseModel = request.RegularModel ?? "gpt-4o";
        TimeSpan timeout = TimeSpan.FromMinutes(request.TimeoutMinutes);
        HttpClient http  = new HttpClient { Timeout = timeout };

        OpenAIClientOptions clientOptions = new OpenAIClientOptions
        {
            Transport      = new HttpClientPipelineTransport(http),
            NetworkTimeout = timeout,
            RetryPolicy    = new ClientRetryPolicy(maxRetries: 0)
        };

        OpenAIClient openAIClient;
        if (IsOllama)
        {
            clientOptions.Endpoint = new Uri("http://localhost:11434/v1");
            openAIClient = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
        }
        else if (!string.IsNullOrWhiteSpace(request.OpenAiEndpoint))
        {
            clientOptions.Endpoint = new Uri(request.OpenAiEndpoint);
            openAIClient = new OpenAIClient(new ApiKeyCredential(request.OpenAiApiKey!), clientOptions);
        }
        else
        {
            openAIClient = new OpenAIClient(new ApiKeyCredential(request.OpenAiApiKey!), clientOptions);
        }

        IChatClient client = openAIClient.GetChatClient(baseModel)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        FileSystemPlugin plugin = new FileSystemPlugin(request.SourcePath, templates.TransmuteIgnore);
        IList<AITool> tools =
        [
            AIFunctionFactory.Create(plugin.ListFiles),
            AIFunctionFactory.Create(plugin.ReadFile),
            AIFunctionFactory.Create(plugin.WriteFile),
        ];

        return (client, tools);
    }

    private static TokenUsage ExtractTokenUsage(ChatResponse result)
    {
        UsageDetails? usage = result.Usage;
        if (usage is null) return TokenUsage.Zero;
        return new TokenUsage((int)(usage.InputTokenCount ?? 0L), (int)(usage.OutputTokenCount ?? 0L));
    }

    private static string ResolveModel(PhaseSpec phase, DecomposeRequest request)
    {
        string regular = request.RegularModel ?? "gpt-4o";
        return phase.ModelWeight switch
        {
            "thick" => request.ThickModel ?? regular,
            "thin"  => request.ThinModel  ?? regular,
            _       => regular
        };
    }

    // ── Helpers (same as ClaudeOrchestrator — both parse same JSON format) ─────

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

    /// <summary>
    /// Extracts a JSON array from a response that may contain surrounding prose or markdown.
    /// Strategy (in order):
    ///   1. Fenced code block — ```json ... ``` or ``` ... ```
    ///   2. Bare JSON — the entire trimmed string starts with '['
    ///   3. Embedded array — find the first '[' and the matching closing ']'
    /// Throws if no array can be located.
    /// </summary>
    private static string ExtractJsonArray(string text)
    {
        // 1. Fenced code block
        Match fence = FenceRegex.Match(text);
        if (fence.Success)
        {
            string fenced = fence.Groups[1].Value.Trim();
            if (fenced.StartsWith('[')) return fenced;
        }

        // 2. Entire response is already a bare JSON array
        if (text.StartsWith('[')) return text;

        // 3. Scan for first '[' and walk brackets to find its matching ']'
        int start = text.IndexOf('[');
        if (start >= 0)
        {
            int depth = 0;
            bool inString = false;
            bool escape = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (escape)          { escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"')        { inString = !inString; continue; }
                if (inString)        continue;
                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}') { depth--; if (depth == 0) return text[start..(i + 1)]; }
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
        // Explicit loop avoids a LINQ delegate + enumerator allocation per character.
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
