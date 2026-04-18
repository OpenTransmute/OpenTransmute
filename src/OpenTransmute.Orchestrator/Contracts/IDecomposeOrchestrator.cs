namespace OpenTransmute.Orchestrator.Contracts;

/// <summary>
/// Single interface for all decompose orchestrators.
/// Produces a stream of PhaseEvent records that the caller uses to track progress,
/// update UI state, and record token usage.
///
/// The stream always ends with either PhaseCompleted or PhaseFailed for every phase executed.
/// On failure, PhaseFailed is yielded and the stream terminates — no further phases run.
/// OperationCanceledException propagates normally when ct is cancelled.
/// </summary>
public interface IDecomposeOrchestrator
{
    OrchestratorType Type { get; }

    IAsyncEnumerable<PhaseEvent> RunAsync(DecomposeRequest request, CancellationToken ct = default);
}
