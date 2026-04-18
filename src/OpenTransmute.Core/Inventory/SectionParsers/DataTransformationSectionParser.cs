using System.Text.Json;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory.SectionParsers;

/// <summary>Parses the Data Transformations section of the inventory markdown.</summary>
public class DataTransformationSectionParser : BaseSectionParser
{
    #region Properties

    public override InventoryCategory Category => InventoryCategory.DataTransformation;

    protected override string[] SummaryFields => ["Logic", "Input shape"];

    #endregion

    #region Methods

    protected override string BuildDetailsJson(string e) => JsonSerializer.Serialize(new
    {
        inputShape = ExtractField(e, "Input shape"),
        outputShape = ExtractField(e, "Output shape"),
        logic = ExtractField(e, "Logic"),
        where = ExtractField(e, "Where")
    });

    #endregion
}
