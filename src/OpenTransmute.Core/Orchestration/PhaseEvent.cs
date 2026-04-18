using OpenTransmute.Models;

namespace OpenTransmute.Orchestration;

/// <summary>
/// Discriminated union of all events produced by a <see cref="JobOrchestrator"/> run.
/// Consumed by <c>JobRunner</c> to update job state and drive the UI.
/// </summary>
public abstract record PhaseEvent;

/// <summary>A phase has started executing.</summary>
public record PhaseStarted(int PhaseNumber, string PhaseName) : PhaseEvent;

/// <summary>
/// A phase completed successfully.
/// OutputPath is the primary output file (null for phases that produce no single file, e.g. expansion phases).
/// </summary>
public record PhaseCompleted(int PhaseNumber, string? OutputPath, TokenUsage Tokens) : PhaseEvent;

/// <summary>A phase failed. The run stops after yielding this event.</summary>
public record PhaseFailed(int PhaseNumber, string Error) : PhaseEvent;

/// <summary>
/// A single line of output from the LLM or a progress note from the orchestrator.
/// Used to drive the real-time log panel in the UI.
/// </summary>
public record LogLine(int PhaseNumber, string Text) : PhaseEvent;

/// <summary>An expansion item (e.g. a Phase 3 cluster spec) has started.</summary>
public record ExpansionItemStarted(int PhaseNumber, int ItemIndex, string ItemName) : PhaseEvent;

/// <summary>An expansion item completed. OutputPath is the file written for this item.</summary>
public record ExpansionItemCompleted(int PhaseNumber, int ItemIndex, string OutputPath, TokenUsage Tokens) : PhaseEvent;
