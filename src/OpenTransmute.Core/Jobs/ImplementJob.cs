using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>
/// Live state for a single implement run — takes a spec (compose output) and drives
/// the selected LLM backend to produce working code.
/// Raises <see cref="OnChanged"/> after any mutation to drive UI updates.
/// </summary>
public class ImplementJob : JobBase
{
    #region Constructor

    /// <summary>New job with a fresh ID.</summary>
    public ImplementJob() { }

    /// <summary>Restored job with a known ID (loaded from persistence).</summary>
    public ImplementJob(Guid id) : base(id) { }

    #endregion

    #region Properties

    public ImplementOptions Options { get; init; } = null!;

    #endregion
}
