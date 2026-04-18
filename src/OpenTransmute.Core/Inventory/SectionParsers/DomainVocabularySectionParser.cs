using System.Text.Json;
using OpenTransmute.Models;

namespace OpenTransmute.Inventory.SectionParsers;

/// <summary>Parses the Domain Vocabulary section of the inventory markdown.</summary>
public class DomainVocabularySectionParser : BaseSectionParser
{
    #region Properties

    public override InventoryCategory Category => InventoryCategory.DomainVocabulary;

    #endregion

    #region Methods

    protected override string BuildDetailsJson(string e) => JsonSerializer.Serialize(new
    {
        definition = ExtractField(e, "Definition"),
        whyItMatters = ExtractField(e, "Why it matters")
    });

    #endregion
}
