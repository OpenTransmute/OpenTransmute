using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Models;

namespace OpenTransmute.Orchestration;

/// <summary>
/// Applies the find/replace corrections that a verify run records on failed claims.
/// Operates at the project level against the on-disk decomposition documents, mirroring
/// the web's Apply-Fixes dialog but without a UI. The decomposition <c>.md</c> files on
/// disk are the source of truth — the database phase outputs are kept in sync best-effort
/// so the Blazor UI reflects the same corrected content.
/// </summary>
public sealed class FixService
{
    #region Members

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<FixService> _logger;

    // Verify reports are produced by the LLM; tolerate casing drift on read.
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Match the web dialog's stamp format so the two paths produce identical files.
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    #endregion

    #region Constructor

    public FixService(IDbContextFactory<AppDbContext> dbFactory, ILogger<FixService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Scans every <c>verify-*.json</c> report for the project, applies each pending fix to
    /// the matching decomposition document, and stamps the claim as fixed. When
    /// <paramref name="dryRun"/> is true, nothing is written — the result reports what would
    /// happen instead.
    /// </summary>
    /// <param name="projectName">Decomposition project name (also the verify output directory name).</param>
    /// <param name="outputRoot">Directory containing the <c>Output/</c> tree (normally the working directory).</param>
    /// <param name="dryRun">When true, preview only — no files or database rows are modified.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A structured summary of fixable claims and their outcomes.</returns>
    public async Task<FixRunResult> ApplyProjectFixesAsync(
        string projectName, string outputRoot, bool dryRun, CancellationToken ct)
    {
        string verifyDir = Path.Combine(outputRoot, "Output", "Verification", projectName);
        string decompDir = Path.Combine(outputRoot, "Output", "Decomposition", projectName);

        if (!Directory.Exists(verifyDir))
            return new FixRunResult { VerificationDirExists = false };

        List<PendingFix> pending = CollectPendingFixes(verifyDir, ct);
        List<FixOutcome> outcomes = new();

        // Group by document so each file is read and written exactly once.
        foreach (IGrouping<string, PendingFix> docGroup in pending.GroupBy(f => f.DocumentName))
        {
            ct.ThrowIfCancellationRequested();

            string docName = docGroup.Key;
            string docPath = Path.Combine(decompDir, docName);

            if (!File.Exists(docPath))
            {
                foreach (PendingFix fix in docGroup)
                    outcomes.Add(FixOutcome.Skipped(fix, "Decomposition document not found on disk."));
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(docPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FixService: failed to read {Doc} for {Project}", docName, projectName);
                foreach (PendingFix fix in docGroup)
                    outcomes.Add(FixOutcome.Skipped(fix, $"Could not read document: {ex.Message}"));
                continue;
            }

            bool changed = false;
            List<PendingFix> applied = new();

            foreach (PendingFix fix in docGroup)
            {
                // Exact-match required — the verify "find" text is verbatim from the document.
                // A miss means the doc was already corrected or has drifted since verification.
                if (!content.Contains(fix.Find, StringComparison.Ordinal))
                {
                    outcomes.Add(FixOutcome.Skipped(fix,
                        "Find text not present — may already be fixed or the document changed."));
                    continue;
                }

                if (!dryRun)
                {
                    content = content.Replace(fix.Find, fix.Replace, StringComparison.Ordinal);
                    changed = true;
                }

                applied.Add(fix);
                outcomes.Add(FixOutcome.Applied(fix, dryRun));
            }

            if (changed && !dryRun)
            {
                await PersistDocumentAsync(projectName, docName, docPath, content, ct);
                await StampVerifyReportAsync(docGroup, applied, ct);
            }
        }

        return new FixRunResult
        {
            VerificationDirExists = true,
            TotalFixable = pending.Count,
            DryRun = dryRun,
            Outcomes = outcomes
        };
    }

    /// <summary>
    /// Reads every verify report in the directory and collects the claims that carry an
    /// unapplied, mechanically-applicable fix.
    /// </summary>
    private List<PendingFix> CollectPendingFixes(string verifyDir, CancellationToken ct)
    {
        List<PendingFix> pending = new();

        foreach (string verifyPath in Directory.GetFiles(verifyDir, "verify-*.json"))
        {
            ct.ThrowIfCancellationRequested();

            VerifyReport? report;
            try
            {
                string json = File.ReadAllText(verifyPath);
                report = JsonSerializer.Deserialize<VerifyReport>(json, _readOptions);
            }
            catch (Exception ex)
            {
                // A malformed report should not abort the whole run — log and move on.
                _logger.LogWarning(ex, "FixService: failed to parse verify report {Path}", verifyPath);
                continue;
            }

            if (report?.Claims is null || report.Claims.Count == 0)
                continue;

            string verifyFileName = Path.GetFileNameWithoutExtension(verifyPath);
            string docName = verifyFileName.StartsWith("verify-", StringComparison.OrdinalIgnoreCase)
                ? verifyFileName[7..] + ".md"
                : verifyFileName + ".md";

            foreach (VerifyClaim claim in report.Claims)
            {
                if (claim.Status is not ("FAIL" or "FABRICATED")) continue;
                if (claim.FixedAt is not null) continue;
                if (claim.Fix is null ||
                    string.IsNullOrWhiteSpace(claim.Fix.Find) ||
                    string.IsNullOrWhiteSpace(claim.Fix.Replace))
                    continue;

                pending.Add(new PendingFix
                {
                    DocumentName   = docName,
                    VerifyFilePath = verifyPath,
                    ClaimId        = claim.Id,
                    Claim          = claim.Claim,
                    Severity       = claim.Severity ?? "FAIL",
                    Finding        = claim.Finding ?? string.Empty,
                    Find           = claim.Fix.Find,
                    Replace        = claim.Fix.Replace
                });
            }
        }

        return pending;
    }

    /// <summary>
    /// Writes the corrected content to disk and syncs the matching database phase output
    /// so the web UI sees the same text. Database failures are non-fatal — the on-disk
    /// document is authoritative.
    /// </summary>
    private async Task PersistDocumentAsync(
        string projectName, string docName, string docPath, string content, CancellationToken ct)
    {
        try
        {
            await File.WriteAllTextAsync(docPath, content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FixService: failed to write {Doc} for {Project}", docName, projectName);
            return;
        }

        try
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync(ct);
            DecomposedProject? project = await db.Projects
                .FirstOrDefaultAsync(p => p.Name == projectName, ct);
            if (project is null) return;

            DecomposePhaseOutput? phase = await db.PhaseOutputs
                .FirstOrDefaultAsync(p => p.ProjectId == project.Id && p.Filename == docName, ct);
            if (phase is null) return;

            phase.Content = content;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FixService: failed to sync phase output {Doc} for {Project}", docName, projectName);
        }
    }

    /// <summary>
    /// Re-reads the verify report, stamps <c>FixedAt</c> on the applied claims, and writes it back.
    /// Non-fatal on failure — the document fixes still stand.
    /// </summary>
    private async Task StampVerifyReportAsync(
        IEnumerable<PendingFix> docGroup, IReadOnlyCollection<PendingFix> applied, CancellationToken ct)
    {
        if (applied.Count == 0) return;

        string verifyPath = docGroup.First().VerifyFilePath;
        HashSet<int> appliedIds = applied.Select(f => f.ClaimId).ToHashSet();

        try
        {
            string json = await File.ReadAllTextAsync(verifyPath, ct);
            VerifyReport? report = JsonSerializer.Deserialize<VerifyReport>(json, _readOptions);
            if (report?.Claims is null) return;

            DateTime now = DateTime.UtcNow;
            foreach (VerifyClaim claim in report.Claims)
            {
                if (appliedIds.Contains(claim.Id))
                    claim.FixedAt = now;
            }

            string updated = JsonSerializer.Serialize(report, _writeOptions);
            await File.WriteAllTextAsync(verifyPath, updated, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FixService: failed to stamp verify report {Path}", verifyPath);
        }
    }

    #endregion
}

/// <summary>
/// A single fixable claim collected from a verify report, flattened for application.
/// </summary>
internal sealed class PendingFix
{
    #region Properties

    public string DocumentName { get; init; } = string.Empty;
    public string VerifyFilePath { get; init; } = string.Empty;
    public int ClaimId { get; init; }
    public string Claim { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Finding { get; init; } = string.Empty;
    public string Find { get; init; } = string.Empty;
    public string Replace { get; init; } = string.Empty;

    #endregion
}

/// <summary>Disposition of a single fix after a run.</summary>
public enum FixOutcomeStatus
{
    /// <summary>The fix was applied and written to disk.</summary>
    Applied,

    /// <summary>The fix would be applied — reported during a dry run only.</summary>
    Previewed,

    /// <summary>The fix was not applied; see <see cref="FixOutcome.SkipReason"/>.</summary>
    Skipped
}

/// <summary>
/// The result of attempting to apply one claim's fix, suitable for console reporting.
/// </summary>
public sealed class FixOutcome
{
    #region Properties

    public string DocumentName { get; init; } = string.Empty;
    public int ClaimId { get; init; }
    public string Severity { get; init; } = string.Empty;
    public string Claim { get; init; } = string.Empty;
    public FixOutcomeStatus Status { get; init; }

    /// <summary>Populated only when <see cref="Status"/> is <see cref="FixOutcomeStatus.Skipped"/>.</summary>
    public string? SkipReason { get; init; }

    #endregion

    #region Methods

    internal static FixOutcome Applied(PendingFix fix, bool dryRun) => new()
    {
        DocumentName = fix.DocumentName,
        ClaimId      = fix.ClaimId,
        Severity     = fix.Severity,
        Claim        = fix.Claim,
        Status       = dryRun ? FixOutcomeStatus.Previewed : FixOutcomeStatus.Applied
    };

    internal static FixOutcome Skipped(PendingFix fix, string reason) => new()
    {
        DocumentName = fix.DocumentName,
        ClaimId      = fix.ClaimId,
        Severity     = fix.Severity,
        Claim        = fix.Claim,
        Status       = FixOutcomeStatus.Skipped,
        SkipReason   = reason
    };

    #endregion
}

/// <summary>
/// Summary of a project-level fix run.
/// </summary>
public sealed class FixRunResult
{
    #region Properties

    /// <summary>False when the project has no verification directory — nothing to fix.</summary>
    public bool VerificationDirExists { get; init; }

    /// <summary>True when the run was a preview that wrote nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>Total claims that carried an unapplied, applicable fix.</summary>
    public int TotalFixable { get; init; }

    /// <summary>Per-claim outcomes in document order.</summary>
    public IReadOnlyList<FixOutcome> Outcomes { get; init; } = Array.Empty<FixOutcome>();

    /// <summary>Count of fixes applied (or that would apply, in a dry run).</summary>
    public int AppliedCount => Outcomes.Count(o => o.Status is FixOutcomeStatus.Applied or FixOutcomeStatus.Previewed);

    /// <summary>Count of fixes skipped.</summary>
    public int SkippedCount => Outcomes.Count(o => o.Status == FixOutcomeStatus.Skipped);

    #endregion
}
