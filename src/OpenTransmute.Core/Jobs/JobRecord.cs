using System.Text.Json.Serialization;
using OpenTransmute.Models;
using OpenTransmute.Orchestrator.Contracts;

namespace OpenTransmute.Jobs;

/// <summary>
/// Serializable snapshot of a DecomposeJob written to codeMap/&lt;project&gt;/job.json.
/// Loaded at startup so job history survives app restarts.
/// </summary>
public class JobRecord
{
    #region Properties

    public Guid Id { get; set; }
    public JobStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public TokenUsage TotalTokens { get; set; } = TokenUsage.Zero;
    public DecomposeOptions Options { get; set; } = null!;
    public PhaseRecord[] Phases { get; set; } = [];
    public string[] LogLines { get; set; } = [];

    #endregion

    #region Methods

    /// <summary>Creates a serializable snapshot from a live <see cref="DecomposeJob"/>.</summary>
    public static JobRecord From(DecomposeJob job) => new()
    {
        Id          = job.Id,
        Status      = job.Status,
        CreatedAt   = job.CreatedAt,
        StartedAt   = job.StartedAt,
        CompletedAt = job.CompletedAt,
        ErrorMessage = job.ErrorMessage,
        TotalTokens = job.TotalTokens,
        Options     = job.Options,
        LogLines    = job.GetLogSnapshot(),
        Phases      = job.Phases.Select(p => new PhaseRecord
        {
            PhaseNumber   = p.PhaseNumber,
            PhaseName     = p.PhaseName,
            Status        = p.Status,
            StartedAt     = p.StartedAt,
            CompletedAt   = p.CompletedAt,
            OutputPreview = p.OutputPreview,
            ErrorMessage  = p.ErrorMessage,
            Tokens        = p.Tokens
        }).ToArray()
    };

    /// <summary>
    /// Reconstructs a <see cref="DecomposeJob"/> from this record.
    /// The resulting job is always in a terminal state — never Running.
    /// </summary>
    public DecomposeJob ToJob()
    {
        DecomposeJob job = new DecomposeJob(Id)
        {
            Options      = Options,
            Status       = Status,
            StartedAt    = StartedAt,
            CompletedAt  = CompletedAt,
            ErrorMessage = ErrorMessage,
            TotalTokens  = TotalTokens
        };

        foreach (string line in LogLines)
            job.AppendLog(line);

        foreach (PhaseRecord pr in Phases)
        {
            PhaseProgress pp = job.Phases[pr.PhaseNumber];
            pp.Status        = pr.Status;
            pp.StartedAt     = pr.StartedAt;
            pp.CompletedAt   = pr.CompletedAt;
            pp.OutputPreview = pr.OutputPreview;
            pp.ErrorMessage  = pr.ErrorMessage;
            pp.Tokens        = pr.Tokens;
        }

        return job;
    }

    #endregion
}

/// <summary>Serializable snapshot of a single phase within a <see cref="JobRecord"/>.</summary>
public class PhaseRecord
{
    #region Properties

    public int PhaseNumber { get; set; }
    public string PhaseName { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public JobStatus Status { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string OutputPreview { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public TokenUsage Tokens { get; set; } = TokenUsage.Zero;

    #endregion
}
