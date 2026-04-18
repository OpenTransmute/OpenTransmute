namespace OpenTransmute.Orchestrator.Model;

/// <summary>
/// Name-keyed registry of model profiles.
/// Lookups are case-insensitive.
/// </summary>
public sealed class ModelRegistry
{
    #region Members

    private readonly Dictionary<string, ModelProfile> _profiles = new();

    #endregion

    #region Methods

    /// <summary>Registers a model profile under the given name (case-insensitive key).</summary>
    public void Add(string name, ModelProfile profile)
        => _profiles[name.ToLowerInvariant()] = profile;

    /// <summary>
    /// Returns the profile registered under <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The model name (case-insensitive).</param>
    /// <returns>The registered <see cref="ModelProfile"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no profile is registered for the given name.</exception>
    public ModelProfile Get(string name)
    {
        if (_profiles.TryGetValue(name.ToLowerInvariant(), out ModelProfile? p))
            return p;

        throw new InvalidOperationException($"Unknown model profile: {name}");
    }

    #endregion
}
