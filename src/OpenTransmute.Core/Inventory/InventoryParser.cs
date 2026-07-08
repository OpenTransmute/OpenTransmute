using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Inventory.SectionParsers;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory;

/// <summary>
/// Parses a decompose-phase inventory markdown file and imports its items into the database.
/// Handles upsert of the project record, removal of old items, section splitting,
/// and deduplication within each category before writing.
/// </summary>
public class InventoryParser(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<InventoryParser> logger)
{
    #region Members

    private static readonly Dictionary<string, InventoryCategory> SectionHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Algorithms"] = InventoryCategory.Algorithm,
        ["Design Patterns"] = InventoryCategory.DesignPattern,
        ["Invariants"] = InventoryCategory.Invariant,
        ["Data Transformations"] = InventoryCategory.DataTransformation,
        ["Domain Vocabulary"] = InventoryCategory.DomainVocabulary,
        ["Architectural Primitives"] = InventoryCategory.ArchitecturalPrimitive,
        ["Key Abstractions"] = InventoryCategory.KeyAbstraction
    };

    private readonly ISectionParser[] _parsers =
    [
        new AlgorithmSectionParser(),
        new DesignPatternSectionParser(),
        new InvariantSectionParser(),
        new DataTransformationSectionParser(),
        new DomainVocabularySectionParser(),
        new ArchitecturalPrimitiveSectionParser(),
        new KeyAbstractionSectionParser()
    ];

    #endregion

    #region Methods

    /// <summary>
    /// Parses the inventory markdown at <paramref name="filePath"/> and upserts all items
    /// into the database under the given project. Existing items for the project are removed
    /// before the new set is written, so this is a full replace, not an incremental merge.
    /// </summary>
    /// <param name="filePath">Absolute path to the inventory markdown file.</param>
    /// <param name="projectName">Short project name used as the database key.</param>
    /// <param name="source">The source path or URL that was analysed to produce this inventory.</param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    public async Task ParseAndImportAsync(string filePath, string projectName, string source, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Inventory file not found: {Path}", filePath);
            return;
        }

        string markdown = await File.ReadAllTextAsync(filePath, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Upsert project record
        DecomposedProject? project = await db.Projects.FirstOrDefaultAsync(p => p.Name == projectName, ct)
            ?? new DecomposedProject { Id = Guid.NewGuid() };

        project.Name = projectName;
        project.Source = source;
        project.OutputPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        project.DecomposedAt = DateTime.UtcNow;

        if (!db.Projects.Local.Contains(project))
            db.Projects.Add(project);

        // Remove old items for this project before re-importing
        IQueryable<InventoryItem> oldItems = db.InventoryItems.Where(i => i.ProjectId == project.Id);
        db.InventoryItems.RemoveRange(oldItems);
        await db.SaveChangesAsync(ct);

        // Parse sections
        List<(string Heading, string Content)> sections = SplitSections(markdown);
        List<InventoryItem> allItems = new List<InventoryItem>();

        foreach ((string heading, string content) in sections)
        {
            if (!SectionHeadings.TryGetValue(heading, out InventoryCategory category)) continue;

            ISectionParser? parser = _parsers.FirstOrDefault(p => p.Category == category);
            if (parser is null) continue;

            List<InventoryItem> items = parser.Parse(content, project.Id).ToList();
            allItems.AddRange(items);
            logger.LogInformation("Parsed {Count} {Category} items from {Project}", items.Count, category, projectName);
        }

        // Deduplicate: within each category keep the first item with a given normalised name.
        // This prevents double-inserts when the LLM emits the same concept twice or a section
        // appears more than once in the markdown.
        HashSet<(InventoryCategory, string)> seen = new HashSet<(InventoryCategory, string)>();
        List<InventoryItem> deduplicated = new List<InventoryItem>(allItems.Count);
        foreach (InventoryItem item in allItems)
        {
            (InventoryCategory, string) key = (item.Category, NormalizeName(item.Name));
            if (seen.Add(key))
            {
                deduplicated.Add(item);
            }
            else
            {
                logger.LogDebug("Skipping duplicate {Category} item '{Name}' in {Project}", item.Category, item.Name, projectName);
            }
        }

        if (deduplicated.Count < allItems.Count)
            logger.LogInformation("Deduplicated {Removed} duplicate items for {Project}", allItems.Count - deduplicated.Count, projectName);

        db.InventoryItems.AddRange(deduplicated);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Imported {Total} inventory items for project {Project}", deduplicated.Count, projectName);
    }

    // Collapse whitespace and lowercase for duplicate detection.
    private static string NormalizeName(string name) =>
        Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");

    /// <summary>
    /// Splits an inventory markdown document into its top-level (<c>##</c>) sections, returning each
    /// section's heading text paired with its body content.
    /// </summary>
    private static List<(string Heading, string Content)> SplitSections(string markdown)
    {
        List<(string, string)> result = new List<(string, string)>();
        string[] parts = Regex.Split(markdown, @"^##\s+", RegexOptions.Multiline);

        foreach (string part in parts.Where(p => p.Trim().Length > 0))
        {
            int newline = part.IndexOf('\n');
            if (newline < 0) continue;

            // Strip leading section number if present: "1. Algorithms" → "Algorithms"
            string heading = Regex.Replace(part[..newline].Trim(), @"^\d+\.\s+", "");
            string content = part[(newline + 1)..].Trim();
            result.Add((heading, content));
        }

        return result;
    }

    #endregion
}
