using System.Text.RegularExpressions;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory;

/// <summary>
/// Base class for all inventory section parsers.
/// Handles H3-block splitting and field extraction from the standard markdown format.
/// Subclasses override <see cref="BuildDetailsJson"/> and optionally <see cref="SummaryFields"/>
/// to match their section's field names.
/// </summary>
public abstract class BaseSectionParser : ISectionParser
{
    #region Properties

    public abstract InventoryCategory Category { get; }

    /// <summary>Field names tried in order to populate the item summary. Subclasses override to match their section format.</summary>
    protected virtual string[] SummaryFields => ["Purpose", "Description", "Definition"];

    #endregion

    #region Methods

    /// <summary>
    /// Parses all H3 entries from <paramref name="sectionMarkdown"/> and yields one
    /// <see cref="InventoryItem"/> per entry.
    /// </summary>
    /// <param name="sectionMarkdown">The body of one ## section from the inventory file.</param>
    /// <param name="projectId">The owning project ID to assign to each item.</param>
    /// <returns>One <see cref="InventoryItem"/> per H3 entry found in the section.</returns>
    public IEnumerable<InventoryItem> Parse(string sectionMarkdown, Guid projectId)
    {
        // Entries are H3 blocks: ### N.N Entry name
        List<string> entries = Regex.Split(sectionMarkdown, @"^###\s+", RegexOptions.Multiline)
            .Where(e => e.Trim().Length > 0)
            .ToList();

        foreach (string entry in entries)
        {
            // First line is the heading, e.g. "1.1 Pattern validation gate"
            int firstNewline = entry.IndexOf('\n');
            string headingLine = (firstNewline >= 0 ? entry[..firstNewline] : entry).Trim();
            string body        = firstNewline >= 0 ? entry[(firstNewline + 1)..] : string.Empty;

            // Strip leading section number: "1.1 Name" or "1. Name" → "Name"
            string name = Regex.Replace(headingLine, @"^\d+[\.\d]*\s+", "").Trim();
            if (string.IsNullOrEmpty(name)) name = "unnamed";

            string summary = SummaryFields
                .Select(f => ExtractField(body, f))
                .FirstOrDefault(v => v is not null)
                ?? string.Empty;

            string? securityScoreRaw = ExtractField(body, "Security score");
            int securityScore = 0;
            if (securityScoreRaw is not null)
            {
                System.Text.RegularExpressions.Match scoreMatch =
                    System.Text.RegularExpressions.Regex.Match(securityScoreRaw, @"\d+");
                if (scoreMatch.Success)
                    int.TryParse(scoreMatch.Value, out securityScore);
            }

            string securityNotes = ExtractField(body, "Security notes") ?? string.Empty;

            yield return new InventoryItem
            {
                Id             = Guid.NewGuid(),
                ProjectId      = projectId,
                Category       = Category,
                Name           = name,
                Summary        = summary.Length > 300 ? summary[..300] : summary.Trim(),
                DetailsJson    = BuildDetailsJson(body),
                RawMarkdown    = StripSecurityFields(entry).Trim(),
                SecurityScore  = Math.Clamp(securityScore, 0, 10),
                SecurityNotes  = securityNotes.Trim(),
                CreatedAt      = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Serializes the section-specific fields of a single entry into the JSON details blob stored
    /// on the inventory item. Subclasses define which fields their section contributes.
    /// </summary>
    protected abstract string BuildDetailsJson(string entryMarkdown);

    /// <summary>
    /// Removes the security score and notes lines from a raw markdown entry.
    /// Because the prompt places them last in every item, we simply truncate
    /// at the first line that contains either field marker.
    /// </summary>
    private static string StripSecurityFields(string entry)
    {
        string[] lines = entry.Split('\n');
        List<string> result = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart('-', ' ', '\t');
            if (trimmed.StartsWith("**Security score:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("**Security notes:", StringComparison.OrdinalIgnoreCase))
                break; // always last — everything from here belongs to the extracted fields
            result.Add(line);
        }
        return string.Join('\n', result).TrimEnd();
    }

    /// <summary>
    /// Extracts the value of a named field from the entry body.
    /// Handles both "**Field:** value" and "- **Field:** value" (bullet-prefixed) formats.
    /// Returns null (not empty string) when the field is absent so ?? chains work correctly.
    /// </summary>
    protected static string? ExtractField(string text, string fieldName)
    {
        // Stops before the next field marker (- **...) or next H3/end-of-string.
        Match m = Regex.Match(text,
            $@"\*\*{Regex.Escape(fieldName)}[:\*]+\*?\s*(.+?)(?=\n\s*-?\s*\*\*|\n###|\z)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string value = m.Groups[1].Value.Trim();
        return value.Length > 0 ? value : null;
    }

    #endregion
}
