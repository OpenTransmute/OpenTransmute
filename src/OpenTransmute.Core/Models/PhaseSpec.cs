using System.Text;

namespace OpenTransmute.Models;

/// <summary>
/// Represents one phase parsed from decompose.md.
/// Drives the phase loop in <c>JobOrchestrator</c> without any per-phase runner classes.
/// </summary>
public sealed class PhaseSpec
{
    #region Properties

    public int    Number      { get; init; }
    public string Title       { get; init; } = string.Empty;
    public string Goal        { get; init; } = string.Empty;

    /// <summary>"normal", "thick", or "thin" — maps to model selection for OpenAI-compatible backends.</summary>
    public string ModelWeight { get; init; } = "normal";

    /// <summary>
    /// Phase numbers whose output files should be injected as prior context.
    /// Parsed from the <c>**Prior Context:**</c> field in decompose.md.
    /// Empty means no prior context is injected for this phase.
    /// </summary>
    public IReadOnlyList<int> PriorContextPhases { get; init; } = [];

    /// <summary>
    /// Default output token ceiling for this phase based on its weight tier.
    /// Used when <c>DecomposeOptions.MaxOutputTokens</c> is 0 (auto).
    /// </summary>
    public int DefaultMaxOutputTokens => ModelWeight switch
    {
        "thick" => 32728,
        "thin"  => 8192,
        _       => 16384
    };

    /// <summary>
    /// Output filename calculated from phase number and title — e.g. "02-initialization-runtime-flow.md".
    /// </summary>
    public string OutputFilename => $"{Number:00}-{TitleSlug(Title)}.md";

    // ── Simple phase ──────────────────────────────────────────────────────────

    /// <summary>The code block content for a simple (non-expansion) phase.</summary>
    public string? Prompt { get; init; }

    // ── Expansion phase (Phase 3) ─────────────────────────────────────────────

    public bool    IsExpansion      { get; init; }
    public string? ExpansionType    { get; init; }   // e.g. "json-array"
    public string? OutputPattern    { get; init; }   // e.g. "codeMap/<project>/03-{index:00}-{slug}.md"
    public string? DiscoveryPrompt  { get; init; }   // first code block — generates the item list
    public string? TemplatePrompt   { get; init; }   // second code block — run once per item

    // ── Synthesis phase (Phase 6) ─────────────────────────────────────────────

    /// <summary>
    /// True when this phase synthesizes outputs from another phase's expansion results,
    /// processing each item separately and then merging. Avoids context exhaustion on
    /// large codebases by chunking the work.
    /// </summary>
    public bool IsSynthesis { get; init; }

    /// <summary>
    /// The phase number whose discovery JSON and output files are consumed by this synthesis.
    /// E.g. <c>3</c> means load <c>03-00-discovery.json</c> and process each 03-xx spec file.
    /// </summary>
    public int? SynthesisSourcePhase { get; init; }

    /// <summary>Per-chunk prompt — run once per source item. First code block in a synthesis phase.</summary>
    public string? ChunkPrompt { get; init; }

    /// <summary>Merge prompt — run once at the end with all partial outputs. Second code block in a synthesis phase.</summary>
    public string? MergePrompt { get; init; }

    // ── Output mode ───────────────────────────────────────────────────────────

    /// <summary>
    /// When true, the model uses an <c>AppendResults</c> tool to write output incrementally
    /// to a temp file instead of streaming to stdout. Parsed from <c>**Output Mode:** append-results</c>.
    /// </summary>
    public bool UseAppendResults { get; init; }

    #endregion

    #region Methods

    private static string TitleSlug(string title)
    {
        StringBuilder sb = new StringBuilder();
        bool prevDash = false;
        foreach (char c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-')
            {
                sb.Append(c);
                prevDash = c == '-';
            }
            else if (!prevDash && sb.Length > 0)
            {
                sb.Append('-');
                prevDash = true;
            }
        }
        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        return sb.ToString();
    }

    #endregion
}
