using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>
/// Live state for a single compose run.
/// Raises <see cref="OnChanged"/> after any state mutation to drive UI updates.
/// </summary>
public class ComposeJob
{
    public ComposeJob() { }
    public ComposeJob(Guid id) { Id = id; }

    #region Properties

    public Guid Id { get; } = Guid.NewGuid();
    public ComposeOptions Options { get; init; } = null!;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string AssembledPrompt { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
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
