using System.Text.Json;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory.SectionParsers;

/// <summary>Parses the Invariants section of the inventory markdown.</summary>
public class InvariantSectionParser : BaseSectionParser
{
    #region Properties

    public override InventoryCategory Category => InventoryCategory.Invariant;

    protected override string[] SummaryFields => ["Condition"];

    #endregion

    #region Methods

    protected override string BuildDetailsJson(string e) => JsonSerializer.Serialize(new
    {
        condition = ExtractField(e, "Condition"),
        whenItMustHold = ExtractField(e, "When it must hold"),
        breakageIfViolated = ExtractField(e, "Breakage if violated")
    });

    #endregion
}
