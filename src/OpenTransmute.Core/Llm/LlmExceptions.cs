namespace OpenTransmute.Llm;

public class LlmRateLimitException(TimeSpan? retryAfter = null)
    : Exception($"LLM rate limit hit. Retry after: {retryAfter?.TotalSeconds ?? 5}s")
{
    public TimeSpan RetryAfter { get; } = retryAfter ?? TimeSpan.FromSeconds(5);
}

public class LlmContextTooLargeException(int estimatedTokens)
    : Exception($"Prompt exceeds context window (estimated {estimatedTokens:N0} tokens).")
{
    public int EstimatedTokens { get; } = estimatedTokens;
}

public class LlmException(int statusCode, string body)
    : Exception($"LLM request failed with HTTP {statusCode}: {body[..Math.Min(200, body.Length)]}")
{
    public int StatusCode { get; } = statusCode;
}
