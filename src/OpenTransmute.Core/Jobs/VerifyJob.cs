using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>
/// Progress snapshot for a single document being verified.
/// </summary>
public class DocumentVerifyProgress
{
    #region Properties

    /// <summary>Zero-based index of this document in the verification run.</summary>
    public int Index { get; set; }

    /// <summary>Filename of the decomposition document being audited.</summary>
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>Current status of this document's verification.</summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>UTC timestamp when verification of this document started.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>UTC timestamp when verification of this document completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Path to the verification report output file.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Preview of the verification report (truncated).</summary>
    public string OutputPreview { get; set; } = string.Empty;

    /// <summary>Error message if verification of this document failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Token usage for this document's verification call.</summary>
    public TokenUsage Tokens { get; set; } = TokenUsage.Zero;

    /// <summary>Elapsed time for this document's verification.</summary>
    public TimeSpan? Elapsed => StartedAt.HasValue
        ? (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value
        : null;

    #endregion
}

/// <summary>
/// Live state for a verify run that audits decomposition output documents
/// against original source code. Each document in the decomposition set
/// produces one verification report.
/// </summary>
public class VerifyJob : JobBase
{
    #region Constructor

    /// <summary>New job with a fresh ID.</summary>
    public VerifyJob() { }

    /// <summary>Restored job with a known ID (loaded from persistence).</summary>
    internal VerifyJob(Guid id) : base(id) { }

    #endregion

    #region Properties

    /// <summary>Options controlling this verify run.</summary>
    public VerifyOptions Options { get; set; } = null!;

    /// <summary>
    /// Per-document progress snapshots, populated when the job starts
    /// after discovering all .md files in the decomposition output directory.
    /// </summary>
    public List<DocumentVerifyProgress> Documents { get; } = new();

    #endregion

    #region Methods

    /// <summary>Marks the given document as running and records its start time.</summary>
    /// <param name="index">Zero-based document index.</param>
    public void DocumentStarted(int index)
    {
        Documents[index].Status = JobStatus.Running;
        Documents[index].StartedAt = DateTime.UtcNow;
        NotifyChanged();
    }

    /// <summary>Marks the given document as completed with token usage and output.</summary>
    /// <param name="index">Zero-based document index.</param>
    /// <param name="outputPath">Path to the verification report.</param>
    /// <param name="outputPreview">Preview of the report content.</param>
    /// <param name="tokens">Token usage for this document.</param>
    public void DocumentCompleted(int index, string? outputPath, string? outputPreview, TokenUsage tokens)
    {
        Documents[index].Status = JobStatus.Completed;
        Documents[index].CompletedAt = DateTime.UtcNow;
        Documents[index].OutputPath = outputPath;
        Documents[index].Tokens = tokens;
        if (outputPreview is not null)
            Documents[index].OutputPreview = outputPreview.Length > 500
                ? outputPreview[..500] + "…"
                : outputPreview;
        TotalTokens += tokens;
        NotifyChanged();
    }

    /// <summary>Marks the given document as failed and records the error message.</summary>
    /// <param name="index">Zero-based document index.</param>
    /// <param name="error">The error message describing the failure.</param>
    public void DocumentFailed(int index, string error)
    {
        Documents[index].Status = JobStatus.Failed;
        Documents[index].CompletedAt = DateTime.UtcNow;
        Documents[index].ErrorMessage = error;
        NotifyChanged();
    }

    #endregion
}
