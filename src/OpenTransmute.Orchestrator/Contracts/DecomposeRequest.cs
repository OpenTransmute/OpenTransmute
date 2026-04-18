namespace OpenTransmute.Orchestrator.Contracts;

/// <summary>
/// All inputs required to run a decompose pipeline.
/// Shared by ClaudeOrchestrator, OpenAiOrchestrator (OpenAI), and OpenAiOrchestrator (Ollama).
/// Each orchestrator reads only the fields relevant to it.
/// </summary>
public sealed class DecomposeRequest
{
    // ── Common ────────────────────────────────────────────────────────────────

    /// <summary>Absolute path to the local source directory to analyse.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Short name for the project (used in output paths and prompts).</summary>
    public required string ProjectName { get; init; }

    /// <summary>Root directory where codeMap/&lt;project&gt;/ output is written.</summary>
    public required string OutputRoot { get; init; }

    /// <summary>First phase to run (0 = fresh run; higher values resume from a checkpoint).</summary>
    public int StartPhase { get; init; } = 0;

    /// <summary>Inclusive last phase to run. Null = run to the end.</summary>
    public int? EndPhase { get; init; }

    /// <summary>Maximum agentic tool-use rounds per phase before the model must produce output.</summary>
    public int MaxTurns { get; init; } = 20;

    /// <summary>
    /// Token ceiling for the model's output per phase call.
    /// 0 = auto — uses per-weight defaults below.
    /// A nonzero value caps all phases at that value (still bounded by the per-weight default).
    /// Only applied by OpenAiOrchestrator; ClaudeCode manages its own output length.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 0;

    /// <summary>Per-weight output token ceilings used when MaxOutputTokens == 0.</summary>
    public int ThickMaxOutputTokens   { get; init; } = 32728;
    public int RegularMaxOutputTokens { get; init; } = 16384;
    public int ThinMaxOutputTokens    { get; init; } = 8192;


    // ── Per-weight model selection (Ollama and OpenAI) ────────────────────────

    /// <summary>
    /// Model name for heavy phases (Phase 3 component specs).
    /// For Ollama: e.g. "devstral". For OpenAI: e.g. "gpt-4.1" or "o3".
    /// Not used by ClaudeCode — it maps to Opus automatically.
    /// </summary>
    public string? ThickModel { get; init; }

    /// <summary>
    /// Model name for normal-weight phases (most phases).
    /// For Ollama: e.g. "devstral". For OpenAI: e.g. "gpt-4.1-mini".
    /// Not used by ClaudeCode — it maps to Sonnet automatically.
    /// </summary>
    public string? RegularModel { get; init; }

    /// <summary>
    /// Model name for light phases.
    /// For Ollama: e.g. "qwen2.5-coder". For OpenAI: e.g. "gpt-4.1-nano".
    /// Not used by ClaudeCode — it maps to Haiku automatically.
    /// </summary>
    public string? ThinModel { get; init; }

    // ── OpenAI / Ollama HTTP transport ────────────────────────────────────────

    /// <summary>HTTP + network timeout in minutes. Applied to both HttpClient and OpenAIClientOptions.NetworkTimeout.</summary>
    public int TimeoutMinutes { get; init; } = 10;

    // ── OpenAI-specific (not used by Ollama or ClaudeCode) ────────────────────

    /// <summary>OpenAI API key. Required when OrchestratorType is OpenAI.</summary>
    public string? OpenAiApiKey { get; init; }

    /// <summary>
    /// Optional custom base URL for OpenAI-compatible endpoints (e.g. Azure OpenAI).
    /// Leave null to use the official OpenAI endpoint.
    /// Ollama always uses http://localhost:11434 — this field is ignored for Ollama.
    /// </summary>
    public string? OpenAiEndpoint { get; init; }

    /// <summary>
    /// Optional free-text hints injected into every phase prompt and the system prompt.
    /// Use to surface domain knowledge, known quirks, or analysis focus areas.
    /// </summary>
    public string? Hints { get; init; }
}
