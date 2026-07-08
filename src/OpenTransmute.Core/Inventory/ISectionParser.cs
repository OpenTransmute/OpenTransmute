using OpenTransmute.Models;

namespace OpenTransmute.Inventory;

/// <summary>
/// Parses one category-specific section of an inventory markdown document into structured
/// <see cref="InventoryItem"/> records. One implementation per <see cref="InventoryCategory"/>.
/// </summary>
public interface ISectionParser
{
    /// <summary>The inventory category this parser is responsible for.</summary>
    InventoryCategory Category { get; }

    /// <summary>
    /// Extracts inventory items from the markdown body of this parser's section, associating each
    /// with the owning project.
    /// </summary>
    IEnumerable<InventoryItem> Parse(string sectionMarkdown, Guid projectId);
}
