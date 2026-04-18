using OpenTransmute.Models;

namespace OpenTransmute.Inventory;

public interface ISectionParser
{
    InventoryCategory Category { get; }
    IEnumerable<InventoryItem> Parse(string sectionMarkdown, Guid projectId);
}
