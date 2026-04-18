namespace OpenTransmute.Models;

/// <summary>
/// Persisted record of a completed (or failed) compose job stored in the database.
/// </summary>
public class SavedComposeJob
{
    public Guid Id { get; set; }
    public string OutputName { get; set; } = string.Empty;
    public string? SourceLabel { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string Output { get; set; } = string.Empty;
    public string OrchestratorType { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string LogLinesJson { get; set; } = "[]";
}
