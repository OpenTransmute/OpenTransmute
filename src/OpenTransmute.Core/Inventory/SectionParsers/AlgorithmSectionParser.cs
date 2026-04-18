using System.Text.Json;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory.SectionParsers;

/// <summary>Parses the Algorithms section of the inventory markdown.</summary>
public class AlgorithmSectionParser : BaseSectionParser
{
    #region Properties

    public override InventoryCategory Category => InventoryCategory.Algorithm;

    #endregion

    #region Methods

    protected override string BuildDetailsJson(string e) => JsonSerializer.Serialize(new
    {
        inputs = ExtractField(e, "Inputs"),
        outputs = ExtractField(e, "Outputs"),
        timeAndSpace = ExtractField(e, "Time / space"),
        pseudocode = ExtractField(e, "Pseudocode"),
        where = ExtractField(e, "Where")
    });

    #endregion
}
