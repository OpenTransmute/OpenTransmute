namespace OpenTransmute.Orchestrator.Exceptions;

public class OrchestratorException(string message, Exception? inner = null)
    : Exception(message, inner);

public class OrchestratorRateLimitException(TimeSpan? retryAfter = null)
    : OrchestratorException($"Rate limit hit. Retry after: {retryAfter?.TotalSeconds ?? 5}s")
{
    public TimeSpan RetryAfter { get; } = retryAfter ?? TimeSpan.FromSeconds(5);
}

public class OrchestratorContextTooLargeException(int estimatedTokens)
    : OrchestratorException($"Prompt exceeds context window (estimated {estimatedTokens:N0} tokens).")
{
    public int EstimatedTokens { get; } = estimatedTokens;
}
