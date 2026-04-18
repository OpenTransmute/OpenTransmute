using OpenTransmute.Orchestrator.Filtering;
using OpenTransmute.Orchestrator.Model;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenTransmute.Orchestrator.Parsing;

/// <summary>
/// Parses decompose.md from the embedded resource at construction time.
///
/// Produces:
///   SystemPrompt — the preamble before the first ## Phase heading.
///   List&lt;PhaseSpec&gt; — one entry per ## Phase section with all metadata and code blocks.
///   ComposeTemplate — contents of compose.md.
///
/// Phase 3 is automatically detected as an expansion phase via its
/// **Expansion Discovery:** marker, producing a PhaseSpec with IsExpansion=true.
/// </summary>
public sealed class PromptTemplates
{
    #region Members

    private readonly string _systemPrompt;
    private readonly List<PhaseSpec> _phases;
    private readonly string _composeTemplate;
    private readonly string _transmuteGuards;
    private readonly string _transmuteIgnore;

    #endregion

    #region Constructor

    public PromptTemplates()
    {
        Assembly asm = typeof(PromptTemplates).Assembly;
        string decompose = ReadResource(asm, "decompose.md");
        _systemPrompt = ExtractSystemPrompt(decompose);
        _phases = ParsePhases(decompose);
        _composeTemplate = ReadResource(asm, "compose.md");
        _transmuteGuards = ReadResource(asm, "transmute.md");
        _transmuteIgnore = ReadResource(asm, ".transmuteignore");
    }

    #endregion

    #region Properties

    public string SystemPrompt => _systemPrompt;
    public IReadOnlyList<PhaseSpec> Phases => _phases;
    public string ComposeTemplate => _composeTemplate;

    /// <summary>
    /// Guards and helpers injected into Transmute and Implement prompts to prevent
    /// common agentic code-generation failure modes.
    /// </summary>
    public string TransmuteGuards => _transmuteGuards;

    public string TransmuteIgnore => _transmuteIgnore;

    #endregion

    #region Methods

    private static string ReadResource(Assembly asm, string logicalName)
    {
        using Stream stream = asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' not found in {asm.GetName().Name}.");
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    private static string ExtractSystemPrompt(string md)
    {
        Match match = Regex.Match(md, @"^## Phase \d", RegexOptions.Multiline);
        return match.Success ? md[..match.Index].Trim() : md.Trim();
    }

    private static List<PhaseSpec> ParsePhases(string md)
    {
        List<PhaseSpec> list = new List<PhaseSpec>();

        // Each ## Phase section runs until the next ## Phase or end-of-file
        Regex phasePattern = new Regex(
            @"^##\s*Phase\s+(?<num>\d+)\s*[—\-]\s*(?<title>.+?)\r?\n(?<body>[\s\S]*?)(?=^##\s*Phase\s|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match m in phasePattern.Matches(md))
        {
            int num    = int.Parse(m.Groups["num"].Value.Trim());
            string title = m.Groups["title"].Value.Trim();
            string body  = m.Groups["body"].Value;

            string goal        = ExtractBlock(body, @"\*\*Goal:\*\*");
            string modelWeight = ExtractLine(body, @"\*\*Model Weight:\*\*") ?? "normal";
            string priorCtxRaw = ExtractLine(body, @"\*\*Prior Context:\*\*") ?? "none";

            string? expansionDiscovery = ExtractLine(body, @"\*\*Expansion Discovery:\*\*");
            string? expansionTemplate  = ExtractLine(body, @"\*\*Expansion Template:\*\*");
            string? outputPattern      = ExtractLine(body, @"\*\*Expansion Output Pattern:\*\*");

            List<string> codeBlocks             = ExtractCodeBlocks(body);
            IReadOnlyList<int> priorContextPhases = ParsePriorContext(priorCtxRaw);

            bool isExpansion = !string.IsNullOrWhiteSpace(expansionDiscovery)
                              && !string.IsNullOrWhiteSpace(outputPattern)
                              && codeBlocks.Count >= 2;

            PhaseSpec phase;

            if (isExpansion)
            {
                phase = new PhaseSpec
                {
                    Number             = num,
                    Title              = title,
                    Goal               = goal,
                    ModelWeight        = modelWeight.Trim().ToLowerInvariant(),
                    PriorContextPhases = priorContextPhases,
                    IsExpansion        = true,
                    ExpansionType      = expansionDiscovery!.Trim().ToLowerInvariant(),
                    OutputPattern      = outputPattern!.Trim(),
                    DiscoveryPrompt    = codeBlocks[0],
                    TemplatePrompt     = codeBlocks[1]
                };
            }
            else
            {
                phase = new PhaseSpec
                {
                    Number             = num,
                    Title              = title,
                    Goal               = goal,
                    ModelWeight        = modelWeight.Trim().ToLowerInvariant(),
                    PriorContextPhases = priorContextPhases,
                    Prompt             = codeBlocks.FirstOrDefault() ?? string.Empty,
                    IsExpansion        = false
                };
            }

            list.Add(phase);
        }

        return list;
    }

    /// <summary>Extracts a multi-line value that runs until the next blank line.</summary>
    private static string ExtractBlock(string body, string labelPattern)
    {
        Match m = Regex.Match(body,
            labelPattern + @"\s*(?<value>[\s\S]*?)(\r?\n\s*\r?\n|\z)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["value"].Value.Trim() : string.Empty;
    }

    /// <summary>Extracts the single line of text that follows a label marker.</summary>
    private static string? ExtractLine(string body, string labelPattern)
    {
        Match m = Regex.Match(body, labelPattern + @"\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["value"].Value.Trim() : null;
    }

    /// <summary>Parses "none", "1", or "0,1,2,3,4" into a list of phase numbers.</summary>
    private static IReadOnlyList<int> ParsePriorContext(string raw)
    {
        string trimmed = raw.Trim().ToLowerInvariant();
        if (trimmed is "none" or "" ) return [];
        return trimmed.Split(',')
            .Select(s => s.Trim())
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToList();
    }

    /// <summary>Returns the trimmed inner text of every fenced code block in order.</summary>
    private static List<string> ExtractCodeBlocks(string body)
    {
        List<string> blocks = new List<string>();
        foreach (Match m in Regex.Matches(body, @"```[^\n]*\n([\s\S]*?)```", RegexOptions.Singleline))
        {
            string inner = m.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(inner))
                blocks.Add(inner);
        }
        return blocks;
    }

    #endregion
}
