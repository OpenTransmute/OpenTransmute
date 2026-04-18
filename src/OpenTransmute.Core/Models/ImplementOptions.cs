using OpenTransmute.Orchestrator.Contracts;

namespace OpenTransmute.Models;

public class ImplementOptions
{
    /// <summary>The spec content to implement (typically the compose output text).</summary>
    public string SpecContent { get; set; } = string.Empty;

    /// <summary>Absolute path where implementation files will be written.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Optional display name for the job header.</summary>
    public string? Label { get; set; }

    /// <summary>The name of the new project being implemented. Passed to the model so it names things correctly.</summary>
    public string ProjectName { get; set; } = string.Empty;

    public OrchestratorType Orchestrator { get; set; } = OrchestratorType.ClaudeCode;

    /// <summary>Model to use — typically the thick model for maximum capability.</summary>
    public string? Model { get; set; }

    public string? OpenAiApiKey { get; set; }
    public string? OpenAiEndpoint { get; set; }

    /// <summary>Maximum Claude Code tool-use turns. Default 200.</summary>
    public int MaxTurns { get; set; } = 200;

    /// <summary>HTTP timeout for direct completion backends. Default 60 minutes.</summary>
    public int TimeoutMinutes { get; set; } = 60;
}
