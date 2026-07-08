using OpenTransmute.Models;

namespace OpenTransmute.Exploration;

/// <summary>
/// Per-call backend parameters for <see cref="RepoClusterer.ClusterAsync"/>. Carries the values
/// pulled from the user's settings (model, endpoint, key, timeout, context tier) so the clustering
/// call runs against the same backend a decompose job would use.
/// </summary>
/// <remarks>
/// Which of these the executor actually reads depends on the backend: the Copilot CLI uses
/// <see cref="Model"/> and <see cref="ContextTier"/> and ignores <see cref="Endpoint"/> /
/// <see cref="ApiKey"/> (it authenticates through GitHub); the OpenAI executor uses
/// <see cref="Endpoint"/> and <see cref="ApiKey"/>. Leaving a field null falls back to the
/// backend default.
/// </remarks>
public sealed record ClusterRequest
{
    /// <summary>Model identifier passed to the backend. Null uses the backend default.</summary>
    public string? Model { get; init; }

    /// <summary>Base-URL override for OpenAI-compatible backends. Ignored by the Copilot CLI.</summary>
    public string? Endpoint { get; init; }

    /// <summary>API key for OpenAI-compatible backends. Ignored by the Copilot CLI (GitHub auth).</summary>
    public string? ApiKey { get; init; }

    /// <summary>Call timeout. Defaults to ten minutes, matching the decompose-job default.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Context-window tier. Only the Copilot backend honors this; a very large digest may warrant
    /// <see cref="LlmContextTier.LongContext"/>.
    /// </summary>
    public LlmContextTier ContextTier { get; init; } = LlmContextTier.Default;
}
