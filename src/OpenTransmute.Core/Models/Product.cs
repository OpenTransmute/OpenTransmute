namespace OpenTransmute.Models;

/// <summary>
/// Groups related <see cref="DecomposedProject"/> instances under a single product umbrella.
/// A product represents a logical system composed of one or more decomposed codebases —
/// e.g. "Asterion" might contain asterion-server, asterion-web, asterion-shared.
/// </summary>
public class Product
{
    #region Properties

    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable product name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of what this product represents.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this product was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Projects belonging to this product.</summary>
    public ICollection<DecomposedProject> Projects { get; set; } = new List<DecomposedProject>();

    #endregion
}
