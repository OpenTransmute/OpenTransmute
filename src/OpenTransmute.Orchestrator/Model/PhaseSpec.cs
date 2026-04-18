using System.Text;

namespace OpenTransmute.Orchestrator.Model;

/// <summary>
/// Represents one phase parsed from decompose.md.
/// Used by both ClaudeOrchestrator and OpenAiOrchestrator to drive the phase loop
/// without any per-phase runner classes.
/// </summary>
public sealed class PhaseSpec
{
    public int    Number      { get; init; }
    public string Title       { get; init; } = string.Empty;
    public string Goal        { get; init; } = string.Empty;

    /// <summary>"normal", "thick", or "thin" — maps to model selection in OpenAiOrchestrator.</summary>
    public string ModelWeight { get; init; } = "normal";

    /// <summary>
    /// Phase numbers whose output files should be injected as prior context.
    /// Parsed from the <c>**Prior Context:**</c> field in decompose.md.
    /// Empty means no prior context is injected for this phase.
    /// </summary>
    public IReadOnlyList<int> PriorContextPhases { get; init; } = [];

    /// <summary>
    /// Default output token ceiling for this phase based on its weight tier.
    /// Used by OpenAiOrchestrator when DecomposeRequest.MaxOutputTokens == 0 (auto).
    /// </summary>
    public int DefaultMaxOutputTokens => ModelWeight switch
    {
        "thick" => 32728,
        "thin"  => 8192,
        _       => 16384
    };

    /// <summary>
    /// Output filename calculated from phase number and title — e.g. "02-initialization-runtime-flow.md".
    /// Never null; no markdown metadata required.
    /// </summary>
    public string OutputFilename => $"{Number:00}-{TitleSlug(Title)}.md";

    // ── Simple phase ──────────────────────────────────────────────────────────

    /// <summary>The code block content for a simple (non-expansion) phase.</summary>
    public string? Prompt { get; init; }

    // ── Expansion phase (Phase 3) ─────────────────────────────────────────────

    public bool    IsExpansion   { get; init; }
    public string? ExpansionType { get; init; }  // e.g. "json-array"
    public string? OutputPattern { get; init; }  // e.g. "codeMap/<project>/03-{index:00}-{slug}.md"
    public string? DiscoveryPrompt  { get; init; }  // first code block — generates the item list
    public string? TemplatePrompt   { get; init; }  // second code block — run once per item

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string TitleSlug(string title)
    {
        var sb = new StringBuilder();
        bool prevDash = false;
        foreach (var c in title.ToLowerInvariant())
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
}
