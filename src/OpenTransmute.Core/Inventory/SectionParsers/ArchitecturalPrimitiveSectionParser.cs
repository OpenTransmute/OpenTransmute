using System.Text.Json;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory.SectionParsers;

/// <summary>Parses the Architectural Primitives section of the inventory markdown.</summary>
public class ArchitecturalPrimitiveSectionParser : BaseSectionParser
{
    #region Properties

    public override InventoryCategory Category => InventoryCategory.ArchitecturalPrimitive;

    protected override string[] SummaryFields => ["What it is"];

    #endregion

    #region Methods

    protected override string BuildDetailsJson(string e) => JsonSerializer.Serialize(new
    {
        whatItIs = ExtractField(e, "What it is"),
        builtOnTopOf = ExtractField(e, "Built on top of"),
        usedBy = ExtractField(e, "Used by")
    });

    #endregion
}
