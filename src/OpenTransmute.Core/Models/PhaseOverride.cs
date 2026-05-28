namespace OpenTransmute.Models;

/// <summary>
/// Per-phase overrides that let the user change the LLM engine and output mode
/// for individual phases without affecting the rest of the pipeline.
/// A <c>null</c> value in any field means "use the global default from <see cref="DecomposeOptions"/>".
/// </summary>
public sealed class PhaseOverride
{
    #region Properties

    /// <summary>
    /// Override the orchestrator engine for this phase.
    /// <c>null</c> = use <see cref="DecomposeOptions.Orchestrator"/>.
    /// </summary>
    public OrchestratorType? Orchestrator { get; set; }

    /// <summary>
    /// Override the AppendResults output mode for this phase.
    /// <c>null</c> = use the phase spec's default from decompose.md.
    /// <c>true</c> = force AppendResults on.
    /// <c>false</c> = force AppendResults off.
    /// </summary>
    public bool? UseAppendResults { get; set; }

    #endregion

    #region Methods

    /// <summary>Returns true when all fields are null — no overrides configured.</summary>
    public bool IsEmpty => Orchestrator is null && UseAppendResults is null;

    #endregion
}
