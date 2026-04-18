using OpenTransmute.Models;
using OpenTransmute.Orchestrator.Contracts;

namespace OpenTransmute.Jobs;

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
public class DecomposeJob
{
    #region Members

    private const int MaxLogLines = 2000;
    private readonly object _logLock = new();
    private readonly List<string> _logLines = new List<string>();

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
    internal DecomposeJob(Guid id) { Id = id; }

    #endregion

    #region Properties

    public Guid Id { get; } = Guid.NewGuid();
    public DecomposeOptions Options { get; init; } = null!;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Accumulated token usage across all phases. Updated as phases complete.</summary>
    public TokenUsage TotalTokens { get; set; } = TokenUsage.Zero;

    public PhaseProgress[] Phases { get; } = Enumerable.Range(0, 8)
        .Select(i => new PhaseProgress { PhaseNumber = i, PhaseName = PhaseNames[i] })
        .ToArray();

    /// <summary>Returns a point-in-time snapshot of log lines safe to iterate on any thread.</summary>
    public string[] GetLogSnapshot() { lock (_logLock) { return [.. _logLines]; } }

    public TimeSpan? TotalElapsed => StartedAt.HasValue
        ? (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value
        : null;

    #endregion

    #region Methods

    public event Action? OnChanged;

    public void NotifyChanged() => OnChanged?.Invoke();

    public void AppendLog(string line)
    {
        lock (_logLock)
        {
            _logLines.Add(line);
            if (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);
        }
        NotifyChanged();
    }

    public void PhaseStarted(int phase)
    {
        Phases[phase].Status = JobStatus.Running;
        Phases[phase].StartedAt = DateTime.UtcNow;
        NotifyChanged();
    }

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

    public void PhaseFailed(int phase, string error)
    {
        Phases[phase].Status = JobStatus.Failed;
        Phases[phase].CompletedAt = DateTime.UtcNow;
        Phases[phase].ErrorMessage = error;
        NotifyChanged();
    }

    #endregion
}
