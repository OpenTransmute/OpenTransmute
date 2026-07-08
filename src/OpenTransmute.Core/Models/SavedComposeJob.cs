namespace OpenTransmute.Models;

/// <summary>
/// Persisted record of a completed (or failed) compose job stored in the database.
/// </summary>
public class SavedComposeJob
{
    #region Properties

    /// <summary>Primary key (matches the in-memory job id).</summary>
    public Guid Id { get; set; }

    /// <summary>Name of the compose output this job produced.</summary>
    public string OutputName { get; set; } = string.Empty;

    /// <summary>Optional label describing the inventory source selection.</summary>
    public string? SourceLabel { get; set; }

    /// <summary>Job status as the integer value of the JobStatus enum.</summary>
    public int Status { get; set; }

    /// <summary>UTC timestamp when the job was created/queued.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp when execution began. Null = never started.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>UTC timestamp when execution finished. Null = still running or never started.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Failure message when the job failed; null on success.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>The composed output markdown.</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Name of the orchestrator engine that ran the job.</summary>
    public string OrchestratorType { get; set; } = string.Empty;

    /// <summary>Model used for the run, if applicable.</summary>
    public string? Model { get; set; }

    /// <summary>Captured log lines serialised as a JSON array.</summary>
    public string LogLinesJson { get; set; } = "[]";

    #endregion
}
