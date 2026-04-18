using OpenTransmute.Orchestrator.Contracts;

namespace OpenTransmute.Cli;

/// <summary>
/// Persisted CLI settings — mirrors LlmSettingsService from the Blazor app.
/// API keys are intentionally excluded and must be supplied at runtime via
/// --api-key or the OPENAI_API_KEY environment variable.
/// </summary>
public sealed class CliSettings
{
    public OrchestratorType Orchestrator    { get; set; } = OrchestratorType.OpenAI;
    public string?          ThickModel      { get; set; }
    public string?          RegularModel    { get; set; }
    public string?          ThinModel       { get; set; }
    public string?          OpenAiEndpoint  { get; set; }
    public int              MaxTurns        { get; set; } = 20;
    public int              MaxOutputTokens { get; set; } = 0;
    public int              TimeoutMinutes  { get; set; } = 10;
    public string?          UserEthos       { get; set; }
}
