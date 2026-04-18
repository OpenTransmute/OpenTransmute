using System.Text.Json.Serialization;

namespace OpenTransmute.Models;

/// <summary>
/// All user-facing inputs for a compose run. Passed from the UI into <see cref="ComposeJob"/>
/// and consumed by <see cref="OpenTransmute.Phases.ComposeOrchestrator"/>.
/// </summary>
public class ComposeOptions
{
    #region Properties

    /// <summary>Display name for this compose output, used in output paths and job headers.</summary>
    public string OutputName { get; set; } = string.Empty;

    /// <summary>IDs of the inventory items to include in the assembled prompt.</summary>
    public List<Guid> SelectedItemIds { get; set; } = new List<Guid>();

    /// <summary>What the target system should be and do.</summary>
    public string? TargetDescription { get; set; }

    /// <summary>Where the system will run (e.g. cloud, browser, embedded, mobile).</summary>
    public string? TargetEnvironment { get; set; }

    /// <summary>Technology stack / language / framework to build for.</summary>
    public string? TargetTechnology { get; set; }

    /// <summary>Which orchestrator engine to use.</summary>
    public OrchestratorType Orchestrator { get; set; } = OrchestratorType.ClaudeCode;

    /// <summary>Model name passed to the backend. Null = backend default.</summary>
    public string? Model { get; set; }

    /// <summary>Optional custom base URL for OpenAI-compatible endpoints (Azure OpenAI, proxies, etc.).</summary>
    public string? OpenAiEndpoint { get; set; }

    /// <summary>OpenAI API key. Required when Orchestrator == OpenAI. Never persisted to disk.</summary>
    [JsonIgnore]
    public string? OpenAiApiKey { get; set; }

    /// <summary>Root directory where Output/Composition/&lt;OutputName&gt;/ is written.</summary>
    public string OutputRoot { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>
    /// When set, bypasses the inventory item lookup and uses this text directly as the
    /// assembled prompt. Used by the Transmute flow to feed decomp spec content into Compose.
    /// </summary>
    public string? PrebuiltPrompt { get; set; }

    /// <summary>Optional label shown in the job detail header instead of the output name.</summary>
    public string? SourceLabel { get; set; }

    /// <summary>
    /// Output token ceiling passed to the backend. Ignored by Claude Code (which manages its own length).
    /// </summary>
    public int MaxOutputTokens { get; set; } = 8192;

    /// <summary>HTTP timeout in minutes for direct completion backends (OpenAI/Ollama). Default 10.</summary>
    public int TimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// User's personal coding standards, naming conventions, error handling philosophy,
    /// testing requirements, etc. Injected as a top-level authoritative instruction in the compose prompt.
    /// </summary>
    public string? Hints { get; set; }

    #endregion
}
