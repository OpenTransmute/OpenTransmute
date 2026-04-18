using System.Text.Json.Serialization;

/// <summary>
/// Carries all inputs required for a single LLM call, regardless of backend.
/// Used by both ClaudeAgentBackend (subprocess) and OpenAiCompletionBackend (HTTP).
/// </summary>
public class LlmRequest
{
    #region Constructor

    public LlmRequest(
        string prompt,
        string? workingDirectory = null,
        string? fileBlock = null,
        int maxOutputTokens = 8192,
        string? model = null,
        string? systemPrompt = null,
        Action<string>? onLogLine = null,
        int maxTurns = 20,
        bool allowFileWrite = false,
        bool useExternal = false,
        string? externalUser = null,
        string? externalEndpoint = null,
        bool useAzureFoundry = false,
        string? azureApiKey = null,
        string? azureEndpoint = null,
        string? openAiApiKey = null,
        string? openAiBaseUrl = null,
        TimeSpan? httpTimeout = null
    )
    {
        Prompt = prompt;
        WorkingDirectory = workingDirectory;
        FileBlock = fileBlock;
        MaxOutputTokens = maxOutputTokens;
        Model = model;
        SystemPrompt = systemPrompt;
        OnLogLine = onLogLine;
        MaxTurns = maxTurns;
        AllowFileWrite = allowFileWrite;
        UseExternal = useExternal;
        ExternalUser = externalUser;
        ExternalEndpoint = externalEndpoint;
        UseAzureFoundry = useAzureFoundry;
        AzureApiKey = azureApiKey;
        AzureEndpoint = azureEndpoint;
        OpenAiApiKey = openAiApiKey;
        OpenAiBaseUrl = openAiBaseUrl;
        if (httpTimeout.HasValue) HttpTimeout = httpTimeout.Value;
    }

    #endregion

    #region Properties

    public string Prompt { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? FileBlock { get; set; }
    public int MaxOutputTokens { get; set; } = 8192;
    public string? Model { get; set; }

    /// <summary>
    /// Appended to the default Claude Code system prompt via --append-system-prompt.
    /// Used to carry the decompose.md preamble into every phase call.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Called for each line of output as it arrives (agent stdout).
    /// Allows the UI to stream progress in real time rather than waiting for completion.
    /// </summary>
    [JsonIgnore]
    public Action<string>? OnLogLine { get; set; }

    /// <summary>
    /// Maximum number of agentic tool-use rounds before the agent must produce output.
    /// Prevents unbounded exploration on large codebases. Default: 20.
    /// </summary>
    public int MaxTurns { get; set; } = 20;

    /// <summary>
    /// When true, suppresses the "do NOT use file-write tools" instruction appended by
    /// ClaudeAgentBackend. Use this when you want Claude Code to write files directly rather
    /// than returning all content via stdout.
    /// </summary>
    public bool AllowFileWrite { get; set; }

    /// <summary>
    /// When true, routes the call through an external OpenAI-compatible endpoint
    /// (e.g. Ollama, LM Studio) by setting ANTHROPIC_BASE_URL before launching the claude CLI.
    /// </summary>
    public bool UseExternal { get; set; }

    /// <summary>Credential passed as ANTHROPIC_AUTH_TOKEN for external endpoints. Typically "ollama".</summary>
    public string? ExternalUser { get; set; }

    /// <summary>Base URL for the external endpoint (e.g. "http://localhost:11434").</summary>
    public string? ExternalEndpoint { get; set; }

    /// <summary>When true, configures Azure AI Foundry env vars instead of Anthropic ones.</summary>
    public bool UseAzureFoundry { get; set; }
    public string? AzureApiKey { get; set; }
    public string? AzureEndpoint { get; set; }

    /// <summary>OpenAI API key for direct HTTP backends (e.g. OpenAiCompletionBackend).</summary>
    public string? OpenAiApiKey { get; set; }

    /// <summary>OpenAI-compatible base URL. Null = https://api.openai.com/v1</summary>
    public string? OpenAiBaseUrl { get; set; }

    /// <summary>HTTP timeout for direct completion backends. Defaults to 10 minutes.</summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(10);

    #endregion
}
