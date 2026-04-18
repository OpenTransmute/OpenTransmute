using System.Text.Json;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory.SectionParsers;

/// <summary>Parses the Design Patterns section of the inventory markdown.</summary>
public class DesignPatternSectionParser : BaseSectionParser
{
    #region Properties

    public override InventoryCategory Category => InventoryCategory.DesignPattern;

    protected override string[] SummaryFields => ["Problem solved"];

    #endregion

    #region Methods

    protected override string BuildDetailsJson(string e) => JsonSerializer.Serialize(new
    {
        where = ExtractField(e, "Where"),
        problemSolved = ExtractField(e, "Problem solved"),
        deviation = ExtractField(e, "Deviation")
    });

    #endregion
}
