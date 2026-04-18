namespace OpenTransmute.Orchestrator.Retry;

/// <summary>
/// Executes an async operation once. All exceptions propagate immediately so the
/// phase fails and the user can decide whether to re-run.
/// </summary>
public sealed class RetryPolicy
{
    #region Methods

    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default) =>
        operation(ct);

    #endregion
}
