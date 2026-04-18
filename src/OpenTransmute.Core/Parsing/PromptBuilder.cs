using System.Text;
using System.Text.Json;
using OpenTransmute.Models;
using OpenTransmute.Orchestration;

namespace OpenTransmute.Parsing;

/// <summary>
/// Builds the final prompt string for each phase by substituting placeholders
/// and prepending prior-output content directly — injecting the file contents
/// rather than listing paths, which eliminates LLM tool-call round-trips.
///
/// Placeholder conventions (from decompose.md):
///   &lt;project&gt;      — project name
///   &lt;path&gt;         — absolute path to the source directory
///   &lt;ListItem.X&gt;   — field X from the current expansion JSON item (e.g. groupName, files)
/// </summary>
public sealed class PromptBuilder(PromptTemplates templates)
{
    #region Properties

    /// <summary>The system prompt (decompose.md preamble).</summary>
    public string SystemPrompt => templates.SystemPrompt;

    #endregion

    #region Methods

    /// <summary>Builds the compose prompt by injecting inventory item blocks, optional target context, and optional user hints.</summary>
    public string BuildComposePrompt(
        IEnumerable<string> itemMarkdownBlocks,
        string? targetDescription = null,
        string? targetEnvironment = null,
        string? targetTechnology = null,
        string? userHints = null)
    {
        string itemsSection = string.Join("\n\n---\n\n", itemMarkdownBlocks);

        List<string> targetLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(targetDescription))
            targetLines.Add($"**Target system:** {targetDescription.Trim()}");
        if (!string.IsNullOrWhiteSpace(targetEnvironment))
            targetLines.Add($"**Deployment environment:** {targetEnvironment.Trim()}");
        if (!string.IsNullOrWhiteSpace(targetTechnology))
            targetLines.Add($"**Technology stack:** {targetTechnology.Trim()}");

        string targetContext = targetLines.Count > 0
            ? "### Target Specification\n\n" + string.Join("\n", targetLines) + "\n\nUse this specification to guide composition choices, technology-specific patterns, and environment constraints."
            : string.Empty;

        return templates.ComposeTemplate
            .Replace("{TargetContext}", targetContext)
            .Replace("{UserEthos}", FormatUserHints(userHints))
            .Replace("{InventoryItems}", itemsSection);
    }

    /// <summary>
    /// Formats the user's hints as a prompt section.
    /// Returns an empty string when <paramref name="userHints"/> is null or whitespace.
    /// </summary>
    public static string FormatUserHints(string? userHints)
    {
        if (string.IsNullOrWhiteSpace(userHints)) return string.Empty;
        return "### Personal Composition Ethos &amp; Style Requirements\n\n" +
               "The following personal coding standards, architectural preferences, naming conventions, " +
               "error handling philosophy, and style requirements have been specified by the user. " +
               "These are authoritative instructions that apply to every composition you produce. " +
               "Do not deviate from them. If a specific composition mode makes a requirement structurally " +
               "impossible, state the conflict explicitly and propose the closest compliant alternative.\n\n" +
               userHints.Trim();
    }

    /// <summary>
    /// Builds the prompt for a simple (non-expansion) phase.
    /// Injects prior phase output content directly into the prompt.
    /// </summary>
    public string BuildSimplePrompt(PhaseSpec phase, RunContext context)
    {
        if (phase.Prompt is null)
            throw new InvalidOperationException($"Phase {phase.Number} is not a simple phase (Prompt is null).");

        string prompt = SubstituteCommon(phase.Prompt, context);
        prompt = PrependPriorContext(prompt, FilterPriorPaths(context.PriorOutputPaths, phase.PriorContextPhases));
        return AppendHints(prompt, context.Hints);
    }

    /// <summary>
    /// Builds the discovery prompt for Step 1 of an expansion phase.
    /// </summary>
    public string BuildExpansionDiscoveryPrompt(PhaseSpec phase, RunContext context)
    {
        if (phase.DiscoveryPrompt is null)
            throw new InvalidOperationException($"Phase {phase.Number} has no DiscoveryPrompt.");

        string prompt = SubstituteCommon(phase.DiscoveryPrompt, context);
        prompt = PrependPriorContext(prompt, FilterPriorPaths(context.PriorOutputPaths, phase.PriorContextPhases));
        return AppendHints(prompt, context.Hints);
    }

    /// <summary>
    /// Builds the per-item prompt for Step 2 of an expansion phase.
    /// Accepts an explicit <paramref name="priorPaths"/> snapshot so all expansion items
    /// receive the same pre-loop context and do not accumulate each other's outputs.
    /// </summary>
    public string BuildExpansionItemPrompt(
        PhaseSpec phase,
        RunContext context,
        JsonElement item,
        IReadOnlyDictionary<string, string> priorPaths)
    {
        if (phase.TemplatePrompt is null)
            throw new InvalidOperationException($"Phase {phase.Number} has no TemplatePrompt.");

        string prompt = SubstituteCommon(phase.TemplatePrompt, context);
        prompt = SubstituteListItem(prompt, item);
        prompt = PrependPriorContext(prompt, FilterPriorPaths(priorPaths, phase.PriorContextPhases));
        return AppendHints(prompt, context.Hints);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Appends user-supplied hints at the end of a prompt so the LLM sees them
    /// after all prior context but before it begins writing its response.
    /// No-ops when hints is null or whitespace.
    /// </summary>
    private static string AppendHints(string prompt, string? hints)
    {
        if (string.IsNullOrWhiteSpace(hints)) return prompt;
        return prompt
            + "\n\n---\n## Analyst Hints\n"
            + "The user has provided the following domain knowledge and hints to assist your analysis.\n"
            + "Treat these as authoritative context — they take precedence over assumptions:\n\n"
            + hints.Trim();
    }

    /// <summary>
    /// Returns only the entries from <paramref name="paths"/> whose filenames start with
    /// one of the allowed phase number prefixes (e.g. "01-" for phase 1, "03-" for phase 3).
    /// An empty <paramref name="phaseNumbers"/> list returns an empty dictionary.
    /// </summary>
    private static IReadOnlyDictionary<string, string> FilterPriorPaths(
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyList<int> phaseNumbers)
    {
        if (phaseNumbers.Count == 0) return new Dictionary<string, string>();
        return paths
            .Where(kvp => phaseNumbers.Any(n => kvp.Key.StartsWith($"{n:00}-")))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static string SubstituteCommon(string template, RunContext context) =>
        template
            .Replace("<project>", context.ProjectName, StringComparison.OrdinalIgnoreCase)
            .Replace("<path>",    context.SourcePath,  StringComparison.OrdinalIgnoreCase);

    private static string SubstituteListItem(string template, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return template;

        string result = template;
        foreach (JsonProperty prop in item.EnumerateObject())
        {
            string placeholder = $"<ListItem.{prop.Name}>";
            string value = prop.Value.ValueKind switch
            {
                JsonValueKind.Array  => string.Join(", ", prop.Value.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => s is not null)),
                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                _                    => prop.Value.ToString()
            };
            result = result.Replace(placeholder, value);
        }
        return result;
    }

    /// <summary>
    /// Reads each prior output file and injects its content directly into the prompt,
    /// eliminating the need for the LLM to make tool-call round-trips to read them.
    ///
    /// Files are processed newest-first so that the most relevant outputs survive
    /// when the total budget is exhausted. Any file that cannot be read falls back
    /// to a path-only reference line.
    /// </summary>
    private static string PrependPriorContext(
        string prompt,
        IReadOnlyDictionary<string, string> priorPaths,
        int maxCharsPerFile = 6000,
        int totalMaxChars = 20000)
    {
        if (priorPaths.Count == 0)
            return prompt;

        StringBuilder sb      = new StringBuilder();
        List<string> omitted  = new List<string>();
        int remaining = totalMaxChars;

        sb.AppendLine("The following prior phase outputs are provided for context:");
        sb.AppendLine();

        // Newest-first so the most-recent outputs always fit within budget
        foreach ((string filename, string path) in priorPaths.Reverse())
        {
            if (remaining <= 0)
            {
                omitted.Add(filename);
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch
            {
                sb.AppendLine($"- {path}  ← could not be read; use your file tools if needed");
                continue;
            }

            string capped = content.Length > maxCharsPerFile
                ? content[..maxCharsPerFile] + "\n... [truncated]"
                : content;

            sb.AppendLine($"=== {filename} ===");
            sb.AppendLine(capped);
            sb.AppendLine();

            remaining -= capped.Length;
        }

        if (omitted.Count > 0)
            sb.AppendLine($"[Older prior outputs omitted to stay within budget: {string.Join(", ", omitted)}]");

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(prompt);

        return sb.ToString();
    }

    #endregion
}
