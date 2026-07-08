namespace OpenTransmute.Models;

/// <summary>
/// Persisted record of a completed (or failed) implement job stored in the database.
/// </summary>
public class SavedImplementJob
{
    #region Properties

    /// <summary>Primary key (matches the in-memory job id).</summary>
    public Guid Id { get; set; }

    /// <summary>Display label for the job, typically the source spec filename.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Absolute output directory where generated code was written.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

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

    /// <summary>Name of the orchestrator engine that ran the job.</summary>
    public string OrchestratorType { get; set; } = string.Empty;

    /// <summary>Model used for the run, if applicable.</summary>
    public string? Model { get; set; }

    /// <summary>Captured log lines serialised as a JSON array.</summary>
    public string LogLinesJson { get; set; } = "[]";

    #endregion
}
