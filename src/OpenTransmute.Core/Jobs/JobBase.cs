using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>
/// Base class for all job types. Carries common identity, status, timing, token usage,
/// and a thread-safe log buffer capped at <see cref="MaxLogLines"/> entries.
/// Raises <see cref="OnChanged"/> after any state mutation to drive UI updates.
/// </summary>
public abstract class JobBase
{
    #region Members

    private const int MaxLogLines = 2000;
    private readonly object _logLock = new();
    private readonly List<string> _logLines = new();

    #endregion

    #region Constructor

    /// <summary>New job with a fresh ID.</summary>
    protected JobBase() { }

    /// <summary>Restored job with a known ID (loaded from persistence).</summary>
    protected JobBase(Guid id) { Id = id; }

    #endregion

    #region Properties

    /// <summary>Unique identifier assigned when the job is created.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Current lifecycle state of the job.</summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>UTC timestamp when the job was created.</summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the job transitioned from <c>Pending</c> to <c>Running</c>.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>UTC timestamp when the job reached a terminal state (Completed or Failed).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Human-readable error description set when <see cref="Status"/> is <see cref="JobStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Accumulated token usage across all LLM calls for this job.</summary>
    public TokenUsage TotalTokens { get; set; } = TokenUsage.Zero;

    /// <summary>Wall time from job start to completion (or now if still running).</summary>
    public TimeSpan? TotalElapsed => StartedAt.HasValue
        ? (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value
        : null;

    #endregion

    #region Methods

    /// <summary>Raised on the calling thread whenever job state or log content changes.</summary>
    public event Action? OnChanged;

    /// <summary>Fires <see cref="OnChanged"/> to notify subscribers of a state change.</summary>
    public void NotifyChanged() => OnChanged?.Invoke();

    /// <summary>
    /// Appends a line to the log buffer, capping at <see cref="MaxLogLines"/> entries.
    /// Thread-safe — may be called from background threads during job execution.
    /// </summary>
    /// <param name="line">The log line to append.</param>
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

    /// <summary>Returns a point-in-time snapshot of log lines safe to iterate on any thread.</summary>
    /// <returns>Array of log lines captured at the moment of the call.</returns>
    public string[] GetLogSnapshot()
    {
        lock (_logLock) { return [.. _logLines]; }
    }

    #endregion
}
