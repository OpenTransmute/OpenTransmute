using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>Job status shared by all job types.</summary>
public enum JobStatus { Pending, Running, Completed, Failed }

/// <summary>
/// Progress snapshot for a single decompose phase.
/// </summary>
public class PhaseProgress
{
    #region Properties

    public int PhaseNumber { get; set; }
    public string PhaseName { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string OutputPreview { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public TokenUsage Tokens { get; set; } = TokenUsage.Zero;

    public TimeSpan? Elapsed => StartedAt.HasValue
        ? (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value
        : null;

    #endregion
}

/// <summary>
/// Live state for a single decompose run, including per-phase progress,
/// accumulated token usage, and the real-time log buffer.
/// Raises <see cref="OnChanged"/> after any state mutation to drive UI updates.
/// </summary>
public class DecomposeJob : JobBase
{
    #region Members

    // Phase names mirror the decompose.md section titles
    private static readonly string[] PhaseNames =
    [
        "Index & Architecture",
        "Structural Survey",
        "Initialization Flow",
        "Component Specifications",
        "Data Formats",
        "Re-implementation Checklist",
        "Composition Inventory",
        "Ethos & Style Fingerprint"
    ];

    #endregion

    #region Constructor

    /// <summary>New job with a fresh ID.</summary>
    public DecomposeJob() { }

    /// <summary>Restored job with a known ID (loaded from disk).</summary>
    internal DecomposeJob(Guid id) : base(id) { }

    #endregion

    #region Properties

    public DecomposeOptions Options { get; set; } = null!;

    /// <summary>
    /// Local filesystem path after source fetching.
    /// Set by JobRunner before RunAsync is called.
    /// </summary>
    public string? LocalSourcePath { get; set; }

    public PhaseProgress[] Phases { get; } = Enumerable.Range(0, 8)
        .Select(i => new PhaseProgress { PhaseNumber = i, PhaseName = PhaseNames[i] })
        .ToArray();

    #endregion

    #region Methods

    /// <summary>Marks the given phase as running and records its start time.</summary>
    /// <param name="phase">Zero-based phase index.</param>
    public void PhaseStarted(int phase)
    {
        Phases[phase].Status = JobStatus.Running;
        Phases[phase].StartedAt = DateTime.UtcNow;
        NotifyChanged();
    }

    /// <summary>Marks the given phase as completed, recording token usage and an output preview.</summary>
    /// <param name="phase">Zero-based phase index.</param>
    /// <param name="outputPreview">Optional preview of the phase output (truncated to 500 chars).</param>
    /// <param name="tokens">Token usage for this phase, added to <see cref="JobBase.TotalTokens"/>.</param>
    public void PhaseCompleted(int phase, string? outputPreview, TokenUsage tokens)
    {
        Phases[phase].Status = JobStatus.Completed;
        Phases[phase].CompletedAt = DateTime.UtcNow;
        Phases[phase].Tokens = tokens;
        if (outputPreview is not null)
            Phases[phase].OutputPreview = outputPreview.Length > 500 ? outputPreview[..500] + "…" : outputPreview;
        TotalTokens += tokens;
        NotifyChanged();
    }

    /// <summary>Marks the given phase as failed and records the error message.</summary>
    /// <param name="phase">Zero-based phase index.</param>
    /// <param name="error">The error message describing the failure.</param>
    public void PhaseFailed(int phase, string error)
    {
        Phases[phase].Status = JobStatus.Failed;
        Phases[phase].CompletedAt = DateTime.UtcNow;
        Phases[phase].ErrorMessage = error;
        NotifyChanged();
    }

    #endregion
}
