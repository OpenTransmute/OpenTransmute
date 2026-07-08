using OpenTransmute.Models;

namespace OpenTransmute.Cli;

/// <summary>
/// Persisted CLI settings — mirrors LlmSettingsService from the Blazor app.
/// API keys are intentionally excluded and must be supplied at runtime via
/// --api-key or the OPENAI_API_KEY environment variable.
/// </summary>
public sealed class CliSettings
{
    /// <summary>Which LLM backend drives jobs by default (ClaudeCode, OpenAI, or Ollama).</summary>
    public OrchestratorType Orchestrator    { get; set; } = OrchestratorType.OpenAI;

    /// <summary>Model used for the heaviest reasoning phases. Null falls back to the backend default.</summary>
    public string?          ThickModel      { get; set; }

    /// <summary>Model used for normal-weight phases. Null falls back to the backend default.</summary>
    public string?          RegularModel    { get; set; }

    /// <summary>Model used for cheap, high-volume phases. Null falls back to the backend default.</summary>
    public string?          ThinModel       { get; set; }

    /// <summary>Override base URL for an OpenAI-compatible endpoint. Null uses the standard OpenAI host.</summary>
    public string?          OpenAiEndpoint  { get; set; }

    /// <summary>Maximum agent turns per LLM call before the run is cut off.</summary>
    public int              MaxTurns        { get; set; } = 20;

    /// <summary>Cap on output tokens per call; <c>0</c> means "use the backend/model default" (no cap).</summary>
    public int              MaxOutputTokens { get; set; } = 0;

    /// <summary>HTTP timeout (minutes) for a single LLM call.</summary>
    public int              TimeoutMinutes  { get; set; } = 10;

    /// <summary>User coding standards/ethos injected as a top-level instruction into every prompt.</summary>
    public string?          UserEthos       { get; set; }
}
