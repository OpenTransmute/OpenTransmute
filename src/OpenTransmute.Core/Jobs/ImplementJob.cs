using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>
/// Live state for a single implement run — takes a spec (compose output) and drives
/// the selected LLM backend to produce working code.
/// Raises <see cref="OnChanged"/> after any mutation to drive UI updates.
/// </summary>
public class ImplementJob
{
    #region Properties

    public Guid Id { get; } = Guid.NewGuid();
    public ImplementOptions Options { get; init; } = null!;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> LogLines { get; } = new();

    public TimeSpan? TotalElapsed => StartedAt.HasValue
        ? (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value
        : null;

    #endregion

    #region Methods

    public event Action? OnChanged;

    public void NotifyChanged() => OnChanged?.Invoke();

    public void AppendLog(string line)
    {
        LogLines.Add(line);
        NotifyChanged();
    }

    #endregion
}
