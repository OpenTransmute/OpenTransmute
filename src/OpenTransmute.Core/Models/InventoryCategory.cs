namespace OpenTransmute.Models;

/// <summary>
/// Classification buckets for inventory items extracted during composition (Phase 6).
/// The LLM assigns each captured concept to exactly one category.
/// </summary>
public enum InventoryCategory
{
    /// <summary>A discrete computational procedure or algorithm.</summary>
    Algorithm,

    /// <summary>A recurring design pattern (e.g. strategy, observer).</summary>
    DesignPattern,

    /// <summary>A condition the system must always preserve.</summary>
    Invariant,

    /// <summary>A mapping or transformation applied to data.</summary>
    DataTransformation,

    /// <summary>A domain-specific term and its meaning.</summary>
    DomainVocabulary,

    /// <summary>A foundational architectural building block.</summary>
    ArchitecturalPrimitive,

    /// <summary>A central abstraction the design hinges on.</summary>
    KeyAbstraction
}
