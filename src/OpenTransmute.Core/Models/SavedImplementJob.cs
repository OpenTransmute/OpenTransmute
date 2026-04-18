namespace OpenTransmute.Models;

public class SavedImplementJob
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string OrchestratorType { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string LogLinesJson { get; set; } = "[]";
}
