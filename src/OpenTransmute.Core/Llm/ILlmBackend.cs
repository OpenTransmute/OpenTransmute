namespace OpenTransmute.Llm;

/// <summary>
/// Used by ComposeOrchestrator to call the claude CLI for composition runs.
/// For decompose, use IDecomposeOrchestrator from the Orchestrator lib.
/// </summary>
public interface ILlmBackend
{
    Task<string> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
