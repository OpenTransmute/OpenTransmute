namespace OpenTransmute.Llm;

/// <summary>
/// All parameters required for a single LLM call. Passed to <see cref="ILlmExecutor.ExecuteAsync"/>.
/// Replaces the former <c>LlmRequest</c> and <c>DecomposeRequest</c> per-backend variants.
/// </summary>
public sealed class LlmExecutionContext
{
    #region Properties

    /// <summary>System prompt injected before the user turn. May be null if not needed.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The user-turn prompt delivered to the model.</summary>
    public string UserPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Model identifier. For Claude subprocess, this is a Claude model name (e.g. "claude-sonnet-4-6").
    /// For OpenAI-compatible backends, this is the deployment or model name (e.g. "gpt-4o").
    /// Null uses the backend default.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>API key for OpenAI-compatible backends. Not used by the Claude subprocess.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Base URL override for OpenAI-compatible backends (e.g. Ollama at http://localhost:11434/v1).
    /// Null uses the standard OpenAI endpoint.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Working directory for the Claude subprocess. The CLI explores this directory with its tools.
    /// Not used by OpenAI-compatible backends.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// When true, read-only file tools (<c>Read</c>, <c>Glob</c>, <c>Grep</c>, <c>LS</c>) are
    /// available to the Claude subprocess. Used by the Decompose operation so the model can
    /// browse the source repository. Ignored by OpenAI-compatible backends.
    /// </summary>
    public bool EnableReadOnlyFileTools { get; init; }

    /// <summary>
    /// When true, file-manipulation tools (<c>Read</c>, <c>Write</c>, <c>Edit</c>, <c>Glob</c>, etc.)
    /// are enabled for the model. Used by the Implement operation.
    /// For the Claude subprocess this restricts <c>--allowedTools</c> to file tools only —
    /// <c>Bash</c> and network tools are never permitted. Default false.
    /// </summary>
    public bool EnableFileTools { get; init; }

    /// <summary>
    /// Root directory exposed to <see cref="FileSystemPlugin"/> when <see cref="EnableFileTools"/> is true.
    /// The plugin restricts all file access to this directory.
    /// </summary>
    public string? FileToolsRoot { get; init; }

    /// <summary>Maximum tool-use turns for the Claude subprocess (--max-turns flag).</summary>
    public int MaxTurns { get; init; } = 10;

    /// <summary>Maximum output tokens. Zero means use the backend default.</summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>HTTP timeout applied to OpenAI-compatible calls. Default 10 minutes.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Content of the project's .transmuteignore file, passed to the file-system plugin
    /// when <see cref="EnableFileTools"/> is true. Null disables ignore filtering.
    /// </summary>
    public string? IgnoreContent { get; init; }

    /// <summary>
    /// When set, the model is instructed to write its output directly to this absolute file path
    /// instead of producing text on stdout. Used by agentic backends (Claude, Copilot) that have
    /// native file-write tools. The orchestrator checks this path after the call completes.
    /// Null = model returns output as text (default for OpenAI/Ollama).
    /// </summary>
    public string? OutputFilePath { get; init; }

    /// <summary>
    /// When true, the executor registers an <c>AppendResults</c> tool that the model calls
    /// incrementally to write output to a temp file. Each tool invocation atomically persists
    /// content to disk, surviving server errors that would kill a streaming stdout response.
    /// The executor reads the temp file on session completion and returns it as the output.
    /// </summary>
    public bool EnableAppendResultsTool { get; init; }

    /// <summary>
    /// Directory where executor stream logs are written. Set by the orchestrator to
    /// <c>Output/Logs/{ProjectName}/</c>. Null disables file-based logging.
    /// </summary>
    public string? LogDirectory { get; init; }

    /// <summary>
    /// Label identifying this call within a run, used in log file names.
    /// Examples: "phase-01", "phase-03-05", "phase-06-merge", "compose", "implement".
    /// </summary>
    public string? PhaseLabel { get; init; }

    /// <summary>
    /// When true, the executor appends JSON-specific output instructions instead of
    /// "plain text to stdout." The model is told to output a single JSON object with
    /// no markdown wrapping, no code fences, and no preamble — start with { and end with }.
    /// </summary>
    public bool JsonOutputMode { get; init; }

    #endregion
}
