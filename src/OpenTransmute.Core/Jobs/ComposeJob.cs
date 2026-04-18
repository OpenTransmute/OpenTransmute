using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>
/// Live state for a single compose run.
/// Raises <see cref="OnChanged"/> after any state mutation to drive UI updates.
/// </summary>
public class ComposeJob : JobBase
{
    #region Constructor

    /// <summary>New job with a fresh ID.</summary>
    public ComposeJob() { }

    /// <summary>Restored job with a known ID (loaded from persistence).</summary>
    public ComposeJob(Guid id) : base(id) { }

    #endregion

    #region Properties

    public ComposeOptions Options { get; init; } = null!;
    public string AssembledPrompt { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;

    #endregion
}
