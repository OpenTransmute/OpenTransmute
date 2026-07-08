namespace OpenTransmute.Models;

/// <summary>Describes the configuration for a single model registration.</summary>
public sealed class ModelProfile
{
    #region Properties

    /// <summary>Backend-specific model identifier (e.g. "gpt-5.4-mini").</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Maximum output tokens to request for calls using this profile.</summary>
    public int MaxTokens { get; init; } = 2048;

    /// <summary>Sampling temperature; lower values favour determinism.</summary>
    public double Temperature { get; init; } = 0.2;

    #endregion
}
