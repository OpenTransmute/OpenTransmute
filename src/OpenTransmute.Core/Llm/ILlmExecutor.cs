using OpenTransmute.Models;

namespace OpenTransmute.Llm;

/// <summary>
/// Executes a single LLM call, yielding output events as they arrive.
/// Implementations cover the Claude CLI subprocess and OpenAI-compatible HTTP APIs.
/// The executor does not throw on LLM failures — failures are delivered as
/// <see cref="LlmFailed"/> events. <see cref="OperationCanceledException"/> is rethrown unchanged.
/// </summary>
public interface ILlmExecutor
{
    /// <summary>The backend type this executor handles.</summary>
    OrchestratorType BackendType { get; }

    /// <summary>
    /// Executes the LLM call described by <paramref name="ctx"/>.
    /// Yields zero or more <see cref="LlmLine"/> events followed by exactly one
    /// <see cref="LlmCompleted"/> or <see cref="LlmFailed"/> terminal event.
    /// </summary>
    /// <param name="ctx">All parameters for this call.</param>
    /// <param name="ct">Cancellation token; propagated to the underlying backend.</param>
    /// <returns>Async stream of output events.</returns>
    IAsyncEnumerable<LlmOutputEvent> ExecuteAsync(LlmExecutionContext ctx, CancellationToken ct);
}
