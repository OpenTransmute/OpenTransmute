namespace OpenTransmute.Models;

/// <summary>
/// Stores the full markdown content produced by a single decompose phase.
/// Written to the database when each phase completes so the spec is queryable
/// independently of the files on disk.
/// </summary>
public class DecomposePhaseOutput
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public DecomposedProject Project { get; set; } = null!;
    public int PhaseNumber { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
