namespace OpenTransmute.Models;

/// <summary>
/// Represents a source project that has been analysed by the decompose pipeline.
/// Owns the inventory items extracted from that project and the per-phase output files.
/// </summary>
public class DecomposedProject
{
    #region Properties

    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Short project name used in output paths and prompts.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Original source — git URL or local folder path.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Absolute path to the root output directory for this project's codeMap files.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the most recent successful decompose run.</summary>
    public DateTime DecomposedAt { get; set; }

    /// <summary>UTC timestamp of the most recent successful verification run. Null = not verified.</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Git commit hash of the source tree at decompose time, if available.</summary>
    public string? CommitHash { get; set; }

    /// <summary>FK to the product this project belongs to. Null = ungrouped.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>The product this project is grouped under.</summary>
    public Product? Product { get; set; }

    /// <summary>Inventory items extracted from this project's composition inventory (Phase 6).</summary>
    public ICollection<InventoryItem> Items { get; set; } = new List<InventoryItem>();

    /// <summary>Raw phase output files stored for re-run and inspection.</summary>
    public ICollection<DecomposePhaseOutput> PhaseOutputs { get; set; } = new List<DecomposePhaseOutput>();

    #endregion
}
