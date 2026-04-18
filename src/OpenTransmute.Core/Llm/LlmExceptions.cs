namespace OpenTransmute.Llm;

/// <summary>
/// Thrown when the LLM backend responds with a rate-limit signal (HTTP 429 or equivalent).
/// Caught by <see cref="OpenTransmute.Retry.RetryPolicy"/> to apply exponential back-off.
/// </summary>
public class LlmRateLimitException(TimeSpan? retryAfter = null)
    : Exception($"LLM rate limit hit. Retry after: {retryAfter?.TotalSeconds ?? 5}s")
{
    /// <summary>Minimum time to wait before the next retry attempt.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter ?? TimeSpan.FromSeconds(5);
}

/// <summary>
/// Thrown when the assembled prompt exceeds the model's context window.
/// Not retried — callers should truncate or split the prompt.
/// </summary>
public class LlmContextTooLargeException(int estimatedTokens)
    : Exception($"Prompt exceeds context window (estimated {estimatedTokens:N0} tokens).")
{
    /// <summary>Best-effort token count estimate at the time the exception was raised.</summary>
    public int EstimatedTokens { get; } = estimatedTokens;
}

/// <summary>
/// Thrown when the LLM backend returns an unexpected HTTP error status that is not
/// a rate limit and not a context-window overflow.
/// </summary>
public class LlmException(int statusCode, string body)
    : Exception($"LLM request failed with HTTP {statusCode}: {body[..Math.Min(200, body.Length)]}")
{
    /// <summary>The HTTP status code returned by the backend.</summary>
    public int StatusCode { get; } = statusCode;
}
