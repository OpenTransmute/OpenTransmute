using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTransmute.Models;
using OpenTransmute.Plugins;

namespace OpenTransmute.Llm;

/// <summary>
/// Executes LLM calls via the OpenAI-compatible API (OpenAI, Ollama, Azure OpenAI) using
/// Microsoft.Extensions.AI. Supports optional tool calling via <see cref="FileSystemPlugin"/>
/// for the Implement operation. One <see cref="HttpClient"/> is shared across all runs;
/// an <see cref="IChatClient"/> is built per-run to accommodate per-run endpoint/model variation.
///
/// Throws <see cref="LlmRateLimitException"/> on rate limit responses so the caller's
/// <see cref="RetryPolicy"/> can retry. All other failures are yielded as <see cref="LlmFailed"/>
/// events. <see cref="OperationCanceledException"/> is rethrown unchanged.
/// </summary>
public sealed class OpenAiChatExecutor : ILlmExecutor
{
    #region Members

    private readonly ILogger<OpenAiChatExecutor> _logger;
    private readonly HttpClient _client;
    private readonly OrchestratorType _backendType;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates the executor with a shared <see cref="HttpClient"/>.
    /// The client uses a 20-minute timeout to accommodate long-running LLM phases.
    /// </summary>
    public OpenAiChatExecutor(ILogger<OpenAiChatExecutor> logger, OrchestratorType backendType = OrchestratorType.OpenAI)
    {
        _logger = logger;
        _client = InitializeClient();
        _backendType = backendType;
    }

    #endregion

    #region Properties

    public OrchestratorType BackendType => _backendType;

    #endregion

    #region Methods

    /// <inheritdoc/>
    public async IAsyncEnumerable<LlmOutputEvent> ExecuteAsync(
        LlmExecutionContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        (IChatClient client, IList<AITool> tools) = BuildChatClient(ctx);

        List<ChatMessage> messages = BuildMessages(ctx);

        ChatOptions options = new ChatOptions
        {
            Tools           = [.. tools],
            ToolMode        = tools.Count > 0 ? ChatToolMode.Auto : null,
            MaxOutputTokens = ctx.MaxOutputTokens > 0 ? ctx.MaxOutputTokens : null,
            ModelId         = ctx.Model,
        };

        // Stream log — captures the full request/response cycle for diagnostics.
        using StreamDiagnosticLog streamLog = StreamDiagnosticLog.Create(ctx, "openai", _logger);
        streamLog.WriteHeader(ctx,
            ("Endpoint", ctx.Endpoint ?? "(default)"),
            ("ToolCount", tools.Count.ToString()));
        streamLog.WriteLine($"=== SYSTEM PROMPT ({ctx.SystemPrompt?.Length ?? 0} chars) ===\n{ctx.SystemPrompt ?? "(none)"}\n");
        streamLog.WriteLine($"=== USER PROMPT ({ctx.UserPrompt.Length:N0} chars) ===\n{ctx.UserPrompt}\n");

        // Capture result and failure outside the try/catch so yields can follow.
        ChatResponse? result = null;
        string? failureMessage = null;

        try
        {
            result = await client.GetResponseAsync(messages, options, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (LlmRateLimitException.IsRateLimitSignal(ex.Message))
        {
            streamLog.WriteLine($"\n\n=== RATE LIMIT ===\n{ex.Message}");
            // Throw so the caller's RetryPolicy can intercept and retry.
            throw new LlmRateLimitException();
        }
        catch (Exception ex)
        {
            // Log only the message — the full exception tree (including inner exceptions and
            // request details from the OpenAI SDK) can contain endpoint URLs or auth tokens.
            _logger.LogError("OpenAI call failed: {Message}", ex.Message);
            failureMessage = ex.Message;
            streamLog.WriteLine($"\n\n=== FAILED ===\n{ex.Message}");
        }

        if (failureMessage is not null)
        {
            yield return new LlmFailed(failureMessage);
            yield break;
        }

        string? content = result!.Text;
        if (string.IsNullOrWhiteSpace(content))
        {
            streamLog.WriteLine("\n\n=== FAILED — empty content ===");
            yield return new LlmFailed("LLM returned empty content.");
            yield break;
        }

        TokenUsage tokens = ExtractTokenUsage(result);
        string tokenLog = $"{tokens.Total:N0} tokens" +
            (tokens.CostUsd.HasValue ? $" (~${tokens.CostUsd:F4})" : string.Empty);

        streamLog.WriteLine($"=== COMPLETED ({content.Length:N0} chars, {tokenLog}) ===");
        streamLog.WriteLine(content);

        yield return new LlmLine(tokenLog);

        yield return new LlmCompleted(content, tokens);
    }

    #endregion

    #region Initialize

    private (IChatClient Client, IList<AITool> Tools) BuildChatClient(LlmExecutionContext ctx)
    {
        string model = ctx.Model ?? "gpt-4o";

        OpenAIClientOptions clientOptions = new OpenAIClientOptions
        {
            Transport      = new HttpClientPipelineTransport(_client),
            NetworkTimeout = ctx.Timeout,
            // Disable SDK retries — the JobOrchestrator's RetryPolicy handles retries.
            RetryPolicy    = new ClientRetryPolicy(maxRetries: 0)
        };

        OpenAIClient openAiClient;
        if (_backendType == OrchestratorType.Ollama)
        {
            // Ollama uses a fixed "ollama" credential and defaults to localhost:11434/v1.
            // The caller may supply a custom endpoint (different host/port) via ctx.Endpoint.
            string ollamaEndpoint = ctx.Endpoint ?? "http://localhost:11434/v1";
            clientOptions.Endpoint = new Uri(ollamaEndpoint);
            openAiClient = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
        }
        else if (!string.IsNullOrWhiteSpace(ctx.Endpoint))
        {
            clientOptions.Endpoint = new Uri(ctx.Endpoint);
            openAiClient = new OpenAIClient(new ApiKeyCredential(ctx.ApiKey ?? string.Empty), clientOptions);
        }
        else
        {
            openAiClient = new OpenAIClient(new ApiKeyCredential(ctx.ApiKey ?? string.Empty), clientOptions);
        }

        IChatClient chatClient = openAiClient.GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        IList<AITool> tools = BuildTools(ctx);
        return (chatClient, tools);
    }

    private static List<ChatMessage> BuildMessages(LlmExecutionContext ctx)
    {
        List<ChatMessage> messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(ctx.SystemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, ctx.SystemPrompt));
        messages.Add(new ChatMessage(ChatRole.User, ctx.UserPrompt));
        return messages;
    }

    private static IList<AITool> BuildTools(LlmExecutionContext ctx)
    {
        if (!ctx.EnableFileTools || ctx.FileToolsRoot is null)
            return Array.Empty<AITool>();

        FileSystemPlugin plugin = new FileSystemPlugin(
            ctx.FileToolsRoot,
            ctx.IgnoreContent ?? string.Empty);

        return
        [
            AIFunctionFactory.Create(plugin.ListFiles),
            AIFunctionFactory.Create(plugin.ReadFile),
            AIFunctionFactory.Create(plugin.WriteFile),
        ];
    }

    private static TokenUsage ExtractTokenUsage(ChatResponse result)
    {
        UsageDetails? usage = result.Usage;
        if (usage is null) return TokenUsage.Zero;
        return new TokenUsage(
            (int)(usage.InputTokenCount  ?? 0L),
            (int)(usage.OutputTokenCount ?? 0L));
    }

    /// <summary>
    /// Creates the shared HttpClient with a 20-minute timeout — long enough for
    /// any single LLM phase without requiring per-run client creation.
    /// </summary>
    private static HttpClient InitializeClient()
    {
        HttpClientHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        HttpClient result = new HttpClient(handler);
        result.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        result.DefaultRequestHeaders.AcceptEncoding.Add(
            new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));
        result.Timeout = TimeSpan.FromMinutes(20);
        return result;
    }

    #endregion
}
