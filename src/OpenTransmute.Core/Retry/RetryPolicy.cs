using OpenTransmute.Llm;

namespace OpenTransmute.Retry;

/// <summary>
/// Retries an async string-returning operation on <see cref="LlmRateLimitException"/>
/// with exponential backoff. All other exceptions propagate immediately.
/// </summary>
public class RetryPolicy(ILogger<RetryPolicy> logger)
{
    #region Members

    private static readonly TimeSpan[] Backoffs =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120)
    ];

    #endregion

    #region Methods

    /// <summary>
    /// Executes <paramref name="operation"/> with automatic retry on rate-limit errors.
    /// Waits the longer of the server-specified retry-after or the next backoff tier.
    /// </summary>
    /// <param name="operation">The async operation to execute, receiving a cancellation token.</param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    /// <returns>The string result of the operation on success.</returns>
    public async Task<string> ExecuteAsync(
        Func<CancellationToken, Task<string>> operation,
        CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await operation(ct);
            }
            catch (LlmRateLimitException ex) when (attempt < Backoffs.Length)
            {
                TimeSpan wait = attempt < Backoffs.Length
                    ? TimeSpan.FromSeconds(Math.Max(ex.RetryAfter.TotalSeconds, Backoffs[attempt].TotalSeconds))
                    : ex.RetryAfter;

                logger.LogWarning("Rate limit hit. Waiting {Wait}s before retry {Attempt}/{Max}",
                    wait.TotalSeconds, attempt + 1, Backoffs.Length);

                await Task.Delay(wait, ct);
                attempt++;
            }
            catch (LlmRateLimitException)
            {
                logger.LogError("Rate limit retries exhausted.");
                throw;
            }
        }
    }

    #endregion
}
