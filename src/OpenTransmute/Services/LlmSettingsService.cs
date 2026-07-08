using OpenTransmute.Models;

namespace OpenTransmute.Services;

/// <summary>
/// In-memory singleton holding the user's preferred LLM engine settings.
/// Values are lost on app restart (API keys are intentionally not persisted).
/// </summary>
public sealed class LlmSettingsService
{
    // ── Engine ───────────────────────────────────────────────────────────────
    /// <summary>Which LLM backend drives jobs by default (ClaudeCode, OpenAI, or Ollama).</summary>
    public OrchestratorType Orchestrator { get; set; } = OrchestratorType.OpenAI;

    // ── Model tiers (Decompose) ───────────────────────────────────────────────
    /// <summary>Model for the heaviest reasoning phases. Null falls back to the backend default.</summary>
    public string? ThickModel   { get; set; }

    /// <summary>Model for normal-weight phases. Null falls back to the backend default.</summary>
    public string? RegularModel { get; set; }

    /// <summary>Model for cheap, high-volume phases. Null falls back to the backend default.</summary>
    public string? ThinModel    { get; set; }

    // ── OpenAI / compatible endpoint ─────────────────────────────────────────
    /// <summary>OpenAI (or compatible) API key. In-memory only — never persisted to disk.</summary>
    public string? OpenAiApiKey  { get; set; }

    /// <summary>Override base URL for an OpenAI-compatible endpoint. Null uses the standard OpenAI host.</summary>
    public string? OpenAiEndpoint { get; set; }

    // ── Decompose run defaults ────────────────────────────────────────────────
    /// <summary>Maximum agent turns per LLM call before the run is cut off.</summary>
    public int MaxTurns        { get; set; } = 20;

    /// <summary>Global output-token cap; <c>0</c> defers to the per-tier ceilings below.</summary>
    public int MaxOutputTokens { get; set; } = 0;

    // Per-weight output token ceilings (used when MaxOutputTokens == 0)
    /// <summary>Output-token ceiling for thick-tier calls when <see cref="MaxOutputTokens"/> is 0.</summary>
    public int ThickMaxOutputTokens   { get; set; } = 32728;

    /// <summary>Output-token ceiling for regular-tier calls when <see cref="MaxOutputTokens"/> is 0.</summary>
    public int RegularMaxOutputTokens { get; set; } = 16384;

    /// <summary>Output-token ceiling for thin-tier calls when <see cref="MaxOutputTokens"/> is 0.</summary>
    public int ThinMaxOutputTokens    { get; set; } = 8192;

    // Context-window tier for the Copilot CLI path. Default = ~200k; LongContext = expanded window.
    /// <summary>Context-window tier for the Copilot CLI path (Default ~200k, LongContext expanded).</summary>
    public LlmContextTier ContextTier { get; set; } = LlmContextTier.Default;

    // ── HTTP timeout (Compose / Ollama / OpenAI) ─────────────────────────────
    /// <summary>HTTP timeout (minutes) for a single LLM call.</summary>
    public int TimeoutMinutes { get; set; } = 10;

    // ── Personal composition ethos ────────────────────────────────────────────
    /// <summary>
    /// Free-form text describing the user's preferred coding style, naming conventions,
    /// error handling philosophy, testing requirements, and any other personal standards.
    /// Injected into every Compose/Transmute prompt as a top-level instruction.
    /// </summary>
    public string? UserEthos { get; set; }
}
