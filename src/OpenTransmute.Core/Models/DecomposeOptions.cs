using System.Text.Json.Serialization;

namespace OpenTransmute.Models;

/// <summary>
/// All user-facing inputs for a decompose run. Carried from the UI or CLI into
/// <see cref="DecomposeJob"/> and then mapped to <see cref="DecomposeRequest"/> for the orchestrator.
/// </summary>
public class DecomposeOptions
{
    #region Properties

    /// <summary>Git URL or local folder path.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Short name used in output paths and prompts. Auto-detected from source if blank.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Which orchestrator engine to use.</summary>
    public OrchestratorType Orchestrator { get; set; } = OrchestratorType.ClaudeCode;

    // ── Ollama + OpenAI: per-weight model names ───────────────────────────────
    // Claude always uses Opus/Sonnet/Haiku — these fields are ignored for ClaudeCode.

    /// <summary>Model for heavy phases (Phase 3 specs). Maps to Opus for Claude.</summary>
    public string? ThickModel { get; set; }

    /// <summary>Model for normal phases. Maps to Sonnet for Claude.</summary>
    public string? RegularModel { get; set; }

    /// <summary>Model for light phases. Maps to Haiku for Claude.</summary>
    public string? ThinModel { get; set; }

    // ── OpenAI-only options ───────────────────────────────────────────────────

    /// <summary>OpenAI API key. Required when Orchestrator == OpenAI. Never persisted to disk.</summary>
    [JsonIgnore]
    public string? OpenAiApiKey { get; set; }

    /// <summary>Optional custom base URL (Azure OpenAI, etc.). Leave blank for official OpenAI.</summary>
    public string? OpenAiEndpoint { get; set; }

    // ── Run control ───────────────────────────────────────────────────────────

    /// <summary>First phase to run. 0 = fresh run; higher values resume from a checkpoint.</summary>
    public int  StartPhase     { get; set; } = 0;

    /// <summary>Inclusive last phase to run. Null = run to the end.</summary>
    public int? EndPhase       { get; set; }

    /// <summary>
    /// 1-based expansion item index to resume from within an expansion phase (e.g. Phase 3).
    /// When set, the discovery step is loaded from the existing 03-00-discovery.json on disk
    /// instead of re-running it, and items below this index are skipped.
    /// 0 or null = run all items from the beginning.
    /// </summary>
    public int? StartItem      { get; set; }

    /// <summary>Maximum agentic tool-use rounds per phase before the model must produce output.</summary>
    public int MaxTurns        { get; set; } = 20;

    /// <summary>
    /// 0 = auto (uses per-weight defaults below).
    /// Nonzero caps all phases at this value (still bounded by per-weight default). No effect for Claude Code.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 0;

    /// <summary>Per-weight output token ceilings used when MaxOutputTokens == 0.</summary>
    public int ThickMaxOutputTokens   { get; set; } = 32728;

    /// <summary>Per-weight output token ceiling for normal-weight phases.</summary>
    public int RegularMaxOutputTokens { get; set; } = 16384;

    /// <summary>Per-weight output token ceiling for light phases.</summary>
    public int ThinMaxOutputTokens    { get; set; } = 8192;

    /// <summary>HTTP + network timeout in minutes. Applied to OpenAI / Ollama requests.</summary>
    public int TimeoutMinutes { get; set; } = 10;

    /// <summary>Root directory where codeMap/&lt;project&gt;/ output is written.</summary>
    public string OutputRoot   { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>
    /// Optional explicit directory for git clones. Null = system temp.
    /// Set when the caller wants a predictable or persistent clone location.
    /// </summary>
    public string? CloneDirectory { get; set; }

    /// <summary>
    /// When true, the temporary clone directory is not deleted after the run.
    /// Useful for debugging or when CloneDirectory points to a persistent location.
    /// </summary>
    public bool KeepClone      { get; set; } = false;

    /// <summary>
    /// Optional free-text hints the user wants injected into every phase prompt.
    /// Use to surface domain knowledge, known quirks, or analysis goals that the
    /// AI would not discover on its own.
    /// </summary>
    public string? Hints { get; set; }

    /// <summary>
    /// Per-phase overrides for engine and output mode. Keyed by phase number (0–7).
    /// Phases not present in the dictionary use the global settings on this options object.
    /// </summary>
    public Dictionary<int, PhaseOverride>? PhaseOverrides { get; set; }

    #endregion

    #region Methods

    /// <summary>
    /// Resolves the orchestrator for a given phase, checking per-phase overrides first.
    /// </summary>
    public OrchestratorType ResolveOrchestrator(int phaseNumber)
    {
        if (PhaseOverrides is not null &&
            PhaseOverrides.TryGetValue(phaseNumber, out PhaseOverride? ov) &&
            ov.Orchestrator is not null)
        {
            return ov.Orchestrator.Value;
        }
        return Orchestrator;
    }

    /// <summary>
    /// Resolves the AppendResults setting for a given phase, checking per-phase overrides first,
    /// then falling back to the phase spec's default from decompose.md.
    /// </summary>
    public bool ResolveAppendResults(int phaseNumber, bool phaseSpecDefault)
    {
        if (PhaseOverrides is not null &&
            PhaseOverrides.TryGetValue(phaseNumber, out PhaseOverride? ov) &&
            ov.UseAppendResults is not null)
        {
            return ov.UseAppendResults.Value;
        }
        return phaseSpecDefault;
    }

    #endregion
}
