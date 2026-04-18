namespace OpenTransmute.Models;

public class InventoryItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public DecomposedProject Project { get; set; } = null!;
    public InventoryCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Category-specific typed fields serialised as JSON.
    /// Shape varies by InventoryCategory — see README for schemas.
    /// </summary>
    public string DetailsJson { get; set; } = "{}";

    /// <summary>Original markdown block extracted from 06-composition-inventory.md.</summary>
    public string RawMarkdown { get; set; } = string.Empty;

    /// <summary>Security risk score 1–10 assigned by the LLM during Phase 6 (1 = no concern, 10 = critical).</summary>
    public int SecurityScore { get; set; }

    /// <summary>LLM-generated security notes describing concerns, attack vectors, or why the score is low.</summary>
    public string SecurityNotes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
