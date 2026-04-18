using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTransmute.Llm;

/// <summary>
/// Calls the OpenAI chat completions endpoint (or any compatible endpoint) directly via HTTP.
/// Used by ComposeOrchestrator when OrchestratorType is OpenAI.
/// A single HttpClient instance is created at construction and reused for all requests.
/// </summary>
public class OpenAiCompletionBackend : ILlmBackend
{
    #region Members

    private readonly ILogger<OpenAiCompletionBackend> _logger;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    #endregion

    #region Constructor

    public OpenAiCompletionBackend(ILogger<OpenAiCompletionBackend> logger)
    {
        _logger = logger;
        _client = InitializeClient();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Sends a prompt to the configured OpenAI-compatible endpoint and returns the response text.
    /// Returns the model's content string, or empty string if the response contained no content.
    /// </summary>
    /// <param name="request">
    /// The LLM request. <see cref="LlmRequest.OpenAiApiKey"/> is required.
    /// <see cref="LlmRequest.OpenAiBaseUrl"/> overrides the default OpenAI endpoint.
    /// </param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    /// <returns>The model's response text, or empty string on empty content.</returns>
    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        string apiKey = request.OpenAiApiKey
            ?? throw new InvalidOperationException("OpenAI API key is required.");

        string baseUrl = string.IsNullOrWhiteSpace(request.OpenAiBaseUrl)
            ? "https://api.openai.com/v1"
            : request.OpenAiBaseUrl.TrimEnd('/');

        string model = request.Model ?? "gpt-4o";

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = new
        {
            model,
            messages = new[] { new { role = "user", content = request.Prompt } },
            max_completion_tokens = request.MaxOutputTokens
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync($"{baseUrl}/chat/completions", body, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("OpenAI completion failed {Status}: {Error}", response.StatusCode, error);
            throw new LlmException((int)response.StatusCode,
                $"OpenAI API error {(int)response.StatusCode}: {error}");
        }

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        string? content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? string.Empty;
    }

    #endregion

    #region Initialize

    /// <summary>
    /// Creates the shared HttpClient with a 10-minute timeout.
    /// One instance per class — never instantiated per request.
    /// </summary>
    private static HttpClient InitializeClient()
    {
        HttpClient result = new HttpClient();
        result.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        result.Timeout = new TimeSpan(0, 10, 0);
        return result;
    }

    #endregion
}
