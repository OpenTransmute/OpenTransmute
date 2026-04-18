namespace OpenTransmute.Orchestrator.Model;

/// <summary>Describes the configuration for a single model registration.</summary>
public sealed class ModelProfile
{
    #region Properties

    public string ModelId { get; init; } = string.Empty;
    public int MaxTokens { get; init; } = 2048;
    public double Temperature { get; init; } = 0.2;

    #endregion
}
