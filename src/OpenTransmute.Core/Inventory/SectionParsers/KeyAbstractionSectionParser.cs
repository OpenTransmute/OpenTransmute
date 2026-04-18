using System.Text.Json;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory.SectionParsers;

/// <summary>Parses the Key Abstractions section of the inventory markdown.</summary>
public class KeyAbstractionSectionParser : BaseSectionParser
{
    #region Properties

    public override InventoryCategory Category => InventoryCategory.KeyAbstraction;

    protected override string[] SummaryFields => ["Hides"];

    #endregion

    #region Methods

    protected override string BuildDetailsJson(string e) => JsonSerializer.Serialize(new
    {
        hides = ExtractField(e, "Hides"),
        exposes = ExtractField(e, "Exposes"),
        breaksIfLeaked = ExtractField(e, "Breaks if leaked")
    });

    #endregion
}
