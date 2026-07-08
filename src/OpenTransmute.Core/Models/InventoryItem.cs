namespace OpenTransmute.Models;

/// <summary>
/// A single capability, component, or concern extracted from a project's composition
/// inventory (Phase 6). Each item is categorised and carries both structured fields
/// (<see cref="DetailsJson"/>) and the original markdown it was parsed from.
/// </summary>
public class InventoryItem
{
    #region Properties

    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning decomposed project.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>The decomposed project this item belongs to.</summary>
    public DecomposedProject Project { get; set; } = null!;

    /// <summary>The inventory category this item was classified under.</summary>
    public InventoryCategory Category { get; set; }

    /// <summary>Human-readable item name as written by the LLM during inventory extraction.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One-line description of what the item is or does.</summary>
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

    /// <summary>UTC timestamp when this item was extracted.</summary>
    public DateTime CreatedAt { get; set; }

    #endregion
}
