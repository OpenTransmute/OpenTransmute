using System.Text;
using OpenTransmute.Models;

namespace OpenTransmute.Orchestration;

/// <summary>
/// Renders structured <see cref="VerifyReport"/> objects into human-readable markdown.
/// All numbers and counts come from the JSON data — nothing is LLM-generated.
/// Three templates: per-document report, project-level scorecard, and remediation plan.
/// </summary>
public static class VerifyReportRenderer
{
    #region Methods

    /// <summary>
    /// Renders a single per-document verification report to markdown.
    /// Header table, failures with fix details, then a compact passed-claims list.
    /// </summary>
    public static string RenderReport(VerifyReport report)
    {
        StringBuilder sb = new();

        // ── Header ────────────────────────────────────────────────────────────
        sb.AppendLine($"# Verification Report: {report.Document}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Claims Audited | {report.Header.ClaimsAudited} |");
        sb.AppendLine($"| Passed | {report.Header.Passed} |");
        sb.AppendLine($"| Minor | {report.Header.Minor} |");
        sb.AppendLine($"| Major | {report.Header.Major} |");
        sb.AppendLine($"| Fabricated | {report.Header.Fabricated} |");

        int fixedCount = report.Claims.Count(c => c.FixedAt is not null);
        if (fixedCount > 0)
            sb.AppendLine($"| Fixed | {fixedCount} |");

        sb.AppendLine($"| Accuracy | {report.Header.AccuracyPercent:F1}% |");
        sb.AppendLine();
        sb.AppendLine($"**Assessment:** {report.Header.OverallAssessment}");
        sb.AppendLine();

        // ── Failures ──────────────────────────────────────────────────────────
        List<VerifyClaim> failures = report.Claims.Where(c => c.Status == "FAIL").ToList();
        if (failures.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## Findings");
            sb.AppendLine();

            foreach (VerifyClaim claim in failures)
            {
                string icon = claim.Severity switch
                {
                    "FABRICATED" => "🔴",
                    "MAJOR"      => "🟠",
                    _            => "🟡"
                };

                sb.AppendLine($"### {icon} #{claim.Id} [{claim.Severity}] — {claim.Section}");
                sb.AppendLine();

                if (claim.FixedAt is not null)
                {
                    sb.AppendLine($"✅ **Fixed** {claim.FixedAt.Value:yyyy-MM-dd HH:mm} UTC");
                    sb.AppendLine();
                }

                sb.AppendLine($"> {claim.Claim}");
                sb.AppendLine();
                sb.AppendLine($"**Finding:** {claim.Finding}");
                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(claim.Actual))
                    sb.AppendLine($"**Actual:** {claim.Actual}");
                if (!string.IsNullOrWhiteSpace(claim.Evidence))
                    sb.AppendLine($"**Evidence:** {claim.Evidence}");
                sb.AppendLine();

                if (claim.Fix is not null)
                {
                    if (!string.IsNullOrWhiteSpace(claim.Location))
                        sb.AppendLine($"**Location:** {claim.Location}");
                    sb.AppendLine();
                    sb.AppendLine("**Find:**");
                    sb.AppendLine("```");
                    sb.AppendLine(claim.Fix.Find);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("**Replace:**");
                    sb.AppendLine("```");
                    sb.AppendLine(claim.Fix.Replace);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine($"**Rationale:** {claim.Fix.Rationale}");
                }

                sb.AppendLine();
            }
        }

        // ── Passed Claims ─────────────────────────────────────────────────────
        List<VerifyClaim> passed = report.Claims.Where(c => c.Status == "PASS").ToList();
        if (passed.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## Passed Claims");
            sb.AppendLine();

            string currentSection = string.Empty;
            foreach (VerifyClaim claim in passed)
            {
                if (!string.Equals(claim.Section, currentSection, StringComparison.Ordinal))
                {
                    currentSection = claim.Section;
                    sb.AppendLine($"**{currentSection}:**");
                }

                sb.AppendLine($"- ✅ {claim.Claim}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the deterministic scorecard and header summaries for the project rollup.
    /// The orchestrator appends LLM prose analysis (systemic issues, themes, etc.) separately.
    /// </summary>
    public static string RenderSummaryScorecard(List<VerifyReport> reports)
    {
        StringBuilder sb = new();

        int totalClaims     = reports.Sum(r => r.Header.ClaimsAudited);
        int totalPassed     = reports.Sum(r => r.Header.Passed);
        int totalMinor      = reports.Sum(r => r.Header.Minor);
        int totalMajor      = reports.Sum(r => r.Header.Major);
        int totalFabricated = reports.Sum(r => r.Header.Fabricated);
        double overallAccuracy = totalClaims > 0
            ? (double)totalPassed / totalClaims * 100.0
            : 100.0;

        // ── Aggregate Totals ──────────────────────────────────────────────────
        sb.AppendLine("# Project Verification Scorecard");
        sb.AppendLine();
        sb.AppendLine("## Aggregate Totals");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Documents Audited | {reports.Count} |");
        sb.AppendLine($"| Total Claims | {totalClaims} |");
        sb.AppendLine($"| Passed | {totalPassed} |");
        sb.AppendLine($"| Minor | {totalMinor} |");
        sb.AppendLine($"| Major | {totalMajor} |");
        sb.AppendLine($"| Fabricated | {totalFabricated} |");
        sb.AppendLine($"| Overall Accuracy | {overallAccuracy:F1}% |");
        sb.AppendLine();

        // ── Per-Document Scorecard ────────────────────────────────────────────
        sb.AppendLine("## Per-Document Scorecard");
        sb.AppendLine();
        sb.AppendLine("| Document | Claims | Passed | Minor | Major | Fabricated | Accuracy |");
        sb.AppendLine("|----------|--------|--------|-------|-------|------------|----------|");

        foreach (VerifyReport report in reports.OrderBy(r => r.Document, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| {report.Document} | {report.Header.ClaimsAudited} | {report.Header.Passed} " +
                $"| {report.Header.Minor} | {report.Header.Major} | {report.Header.Fabricated} " +
                $"| {report.Header.AccuracyPercent:F1}% |");
        }

        sb.AppendLine($"| **TOTAL** | **{totalClaims}** | **{totalPassed}** " +
            $"| **{totalMinor}** | **{totalMajor}** | **{totalFabricated}** " +
            $"| **{overallAccuracy:F1}%** |");
        sb.AppendLine();

        // ── Per-Document Assessments ──────────────────────────────────────────
        sb.AppendLine("## Per-Document Assessments");
        sb.AppendLine();

        foreach (VerifyReport report in reports.OrderBy(r => r.Document, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- **{report.Document}** — {report.Header.OverallAssessment}");

        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Builds a compact failure digest for the rollup LLM — enough to spot
    /// patterns and systemic issues without overwhelming the context.
    /// </summary>
    public static string RenderFailureDigest(List<VerifyReport> reports)
    {
        StringBuilder sb = new();

        foreach (VerifyReport report in reports.OrderBy(r => r.Document, StringComparer.OrdinalIgnoreCase))
        {
            List<VerifyClaim> failures = report.Claims.Where(c => c.Status == "FAIL").ToList();
            if (failures.Count == 0) continue;

            sb.AppendLine($"### {report.Document}");

            foreach (VerifyClaim claim in failures)
                sb.AppendLine($"- [{claim.Severity}] {claim.Finding ?? claim.Claim}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the deterministic remediation plan from all structured reports.
    /// Extracts ALL failed claims with fixes regardless of severity, grouped by document.
    /// No LLM involvement — pure aggregation.
    /// </summary>
    public static string RenderRemediation(List<VerifyReport> reports)
    {
        StringBuilder sb = new();

        // Collect fixable and unfixable claims by document — all severities.
        var docGroups = reports
            .OrderBy(r => r.Document, StringComparer.OrdinalIgnoreCase)
            .Select(r => new
            {
                r.Document,
                Fixes = r.Claims
                    .Where(c => c is { Status: "FAIL" } && c.Fix is not null)
                    .ToList(),
                Unfixable = r.Claims
                    .Where(c => c is { Status: "FAIL" } && c.Fix is null)
                    .ToList()
            })
            .Where(d => d.Fixes.Count > 0 || d.Unfixable.Count > 0)
            .ToList();

        int totalFixes     = docGroups.Sum(d => d.Fixes.Count);
        int totalUnfixable = docGroups.Sum(d => d.Unfixable.Count);

        // ── Summary ───────────────────────────────────────────────────────────
        sb.AppendLine("# Remediation Plan");
        sb.AppendLine();
        sb.AppendLine($"{totalFixes} exact fixes across {docGroups.Count} documents. " +
            (totalUnfixable > 0
                ? $"{totalUnfixable} findings require manual review (no automated fix)."
                : "All findings have automated fixes."));
        sb.AppendLine();

        // ── Fixes by Document ─────────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine();

        int globalFixNum = 0;
        foreach (var doc in docGroups)
        {
            sb.AppendLine($"## {doc.Document}");
            sb.AppendLine();

            foreach (VerifyClaim claim in doc.Fixes)
            {
                globalFixNum++;
                sb.AppendLine($"### Fix {globalFixNum}: {claim.Section}");
                sb.AppendLine();
                sb.AppendLine($"- **Document:** {doc.Document}");
                sb.AppendLine($"- **Severity:** {claim.Severity}");
                sb.AppendLine($"- **Finding:** {claim.Finding}");
                sb.AppendLine($"- **Location:** {claim.Location}");
                sb.AppendLine();
                sb.AppendLine("**Find:**");
                sb.AppendLine("```");
                sb.AppendLine(claim.Fix!.Find);
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("**Replace:**");
                sb.AppendLine("```");
                sb.AppendLine(claim.Fix.Replace);
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine($"**Rationale:** {claim.Fix.Rationale}");
                sb.AppendLine();
            }

            if (doc.Unfixable.Count > 0)
            {
                sb.AppendLine("### Manual Review Required");
                sb.AppendLine();
                foreach (VerifyClaim claim in doc.Unfixable)
                    sb.AppendLine($"- **[{claim.Severity}]** {claim.Finding} *(Section: {claim.Section})*");
                sb.AppendLine();
            }
        }

        // ── Correction Scorecard ──────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Correction Scorecard");
        sb.AppendLine();
        sb.AppendLine("| Document | MAJOR Fixes | FABRICATED Fixes | MINOR Fixes | Unfixable | Total |");
        sb.AppendLine("|----------|-------------|------------------|-------------|-----------|-------|");

        int grandMajor = 0, grandFab = 0, grandMinor = 0, grandUnfixable = 0;
        foreach (VerifyReport report in reports.OrderBy(r => r.Document, StringComparer.OrdinalIgnoreCase))
        {
            int majorFixes = report.Claims.Count(c => c is { Status: "FAIL", Severity: "MAJOR", Fix: not null });
            int fabFixes   = report.Claims.Count(c => c is { Status: "FAIL", Severity: "FABRICATED", Fix: not null });
            int minFixes   = report.Claims.Count(c => c is { Status: "FAIL", Severity: "MINOR", Fix: not null });
            int unfixable  = report.Claims.Count(c => c is { Status: "FAIL", Fix: null });

            if (majorFixes + fabFixes + minFixes + unfixable == 0) continue;

            grandMajor += majorFixes;
            grandFab   += fabFixes;
            grandMinor += minFixes;
            grandUnfixable += unfixable;

            int total = majorFixes + fabFixes + minFixes + unfixable;
            sb.AppendLine($"| {report.Document} | {majorFixes} | {fabFixes} | {minFixes} | {unfixable} | {total} |");
        }

        int grandTotal = grandMajor + grandFab + grandMinor + grandUnfixable;
        sb.AppendLine($"| **TOTAL** | **{grandMajor}** | **{grandFab}** | **{grandMinor}** | **{grandUnfixable}** | **{grandTotal}** |");
        sb.AppendLine();

        return sb.ToString();
    }

    #endregion
}
