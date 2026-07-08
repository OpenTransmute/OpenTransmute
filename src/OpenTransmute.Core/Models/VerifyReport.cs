using System.Text.Json.Serialization;

namespace OpenTransmute.Models;

/// <summary>
/// Structured verification report for a single decomposition document.
/// Produced by the LLM as JSON, consumed deterministically by the rollup
/// and remediation pipeline. Every count is recomputed from the claims
/// array after deserialization — the header values are informational only.
/// </summary>
public class VerifyReport
{
    #region Properties

    /// <summary>Filename of the decomposition document that was audited.</summary>
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;

    /// <summary>Aggregate counts and overall assessment.</summary>
    [JsonPropertyName("header")]
    public VerifyReportHeader Header { get; set; } = new();

    /// <summary>Every audited claim — PASS and FAIL — in document order.</summary>
    [JsonPropertyName("claims")]
    public List<VerifyClaim> Claims { get; set; } = [];

    #endregion

    #region Methods

    /// <summary>
    /// Recomputes header counts from the actual claims list. Call this after
    /// deserialization to guarantee the numbers are correct regardless of
    /// what the model put in the header fields.
    /// </summary>
    public void RecomputeHeader()
    {
        Header.ClaimsAudited = Claims.Count;
        Header.Passed = Claims.Count(c => c.Status == "PASS");
        Header.Minor = Claims.Count(c => c is { Status: "FAIL", Severity: "MINOR" });
        Header.Major = Claims.Count(c => c is { Status: "FAIL", Severity: "MAJOR" });
        Header.Fabricated = Claims.Count(c => c is { Status: "FAIL", Severity: "FABRICATED" });
    }

    #endregion
}

/// <summary>
/// Aggregate counts and assessment for a single document's verification.
/// </summary>
public class VerifyReportHeader
{
    #region Properties

    /// <summary>Total number of claims audited.</summary>
    [JsonPropertyName("claimsAudited")]
    public int ClaimsAudited { get; set; }

    /// <summary>Number of claims that passed verification.</summary>
    [JsonPropertyName("passed")]
    public int Passed { get; set; }

    /// <summary>Number of failing claims with MINOR severity.</summary>
    [JsonPropertyName("minor")]
    public int Minor { get; set; }

    /// <summary>Number of failing claims with MAJOR severity.</summary>
    [JsonPropertyName("major")]
    public int Major { get; set; }

    /// <summary>Number of failing claims flagged as FABRICATED.</summary>
    [JsonPropertyName("fabricated")]
    public int Fabricated { get; set; }

    /// <summary>Free-text overall assessment written by the LLM.</summary>
    [JsonPropertyName("overallAssessment")]
    public string OverallAssessment { get; set; } = string.Empty;

    /// <summary>
    /// Accuracy percentage: passed / claimsAudited * 100.
    /// Computed from counts, not serialized from LLM output.
    /// </summary>
    [JsonIgnore]
    public double AccuracyPercent => ClaimsAudited > 0
        ? (double)Passed / ClaimsAudited * 100.0
        : 100.0;

    #endregion
}

/// <summary>
/// A single audited claim within a verification report.
/// PASS claims carry only id/section/claim/status.
/// FAIL claims carry the full detail including fix info.
/// </summary>
public class VerifyClaim
{
    #region Properties

    /// <summary>Sequential claim number within the report.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Section heading in the decomposition document where the claim appears.</summary>
    [JsonPropertyName("section")]
    public string Section { get; set; } = string.Empty;

    /// <summary>The factual assertion being audited.</summary>
    [JsonPropertyName("claim")]
    public string Claim { get; set; } = string.Empty;

    /// <summary>PASS or FAIL.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "PASS";

    /// <summary>MINOR, MAJOR, or FABRICATED. Null for PASS claims.</summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    /// <summary>What is wrong with this claim.</summary>
    [JsonPropertyName("finding")]
    public string? Finding { get; set; }

    /// <summary>What the source code actually says.</summary>
    [JsonPropertyName("actual")]
    public string? Actual { get; set; }

    /// <summary>File path, line number, or content proving the finding.</summary>
    [JsonPropertyName("evidence")]
    public string? Evidence { get; set; }

    /// <summary>Section heading or position in the document where the wrong text appears.</summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>Exact find/replace fix. Null when automated fix is not possible.</summary>
    [JsonPropertyName("fix")]
    public VerifyClaimFix? Fix { get; set; }

    /// <summary>UTC timestamp when this claim's fix was applied. Null = not yet fixed.</summary>
    [JsonPropertyName("fixedAt")]
    public DateTime? FixedAt { get; set; }

    #endregion
}

/// <summary>
/// Exact text replacement that corrects a failed claim in a decomposition document.
/// </summary>
public class VerifyClaimFix
{
    #region Properties

    /// <summary>Verbatim text from the decomposition document to replace.</summary>
    [JsonPropertyName("find")]
    public string Find { get; set; } = string.Empty;

    /// <summary>Corrected text — must be a drop-in replacement preserving formatting.</summary>
    [JsonPropertyName("replace")]
    public string Replace { get; set; } = string.Empty;

    /// <summary>Why this fix is correct, citing source evidence.</summary>
    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = string.Empty;

    #endregion
}
