namespace OpenTransmute.Models;

/// <summary>
/// Stores the full markdown content produced by a single decompose phase.
/// Written to the database when each phase completes so the spec is queryable
/// independently of the files on disk.
/// </summary>
public class DecomposePhaseOutput
{
    #region Properties

    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning decomposed project.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>The decomposed project this output belongs to.</summary>
    public DecomposedProject Project { get; set; } = null!;

    /// <summary>Zero-based phase number that produced this output.</summary>
    public int PhaseNumber { get; set; }

    /// <summary>Output filename on disk (e.g. 06-composition-inventory.md).</summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>Full markdown content the phase produced.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this phase output was written.</summary>
    public DateTime CreatedAt { get; set; }

    #endregion
}
