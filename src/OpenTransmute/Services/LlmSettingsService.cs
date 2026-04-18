using OpenTransmute.Models;

namespace OpenTransmute.Services;

/// <summary>
/// In-memory singleton holding the user's preferred LLM engine settings.
/// Values are lost on app restart (API keys are intentionally not persisted).
/// </summary>
public sealed class LlmSettingsService
{
    // ── Engine ───────────────────────────────────────────────────────────────
    public OrchestratorType Orchestrator { get; set; } = OrchestratorType.OpenAI;

    // ── Model tiers (Decompose) ───────────────────────────────────────────────
    public string? ThickModel   { get; set; }
    public string? RegularModel { get; set; }
    public string? ThinModel    { get; set; }

    // ── OpenAI / compatible endpoint ─────────────────────────────────────────
    public string? OpenAiApiKey  { get; set; }
    public string? OpenAiEndpoint { get; set; }

    // ── Decompose run defaults ────────────────────────────────────────────────
    public int MaxTurns        { get; set; } = 20;
    public int MaxOutputTokens { get; set; } = 0;

    // Per-weight output token ceilings (used when MaxOutputTokens == 0)
    public int ThickMaxOutputTokens   { get; set; } = 32728;
    public int RegularMaxOutputTokens { get; set; } = 16384;
    public int ThinMaxOutputTokens    { get; set; } = 8192;

    // ── HTTP timeout (Compose / Ollama / OpenAI) ─────────────────────────────
    public int TimeoutMinutes { get; set; } = 10;

    // ── Personal composition ethos ────────────────────────────────────────────
    /// <summary>
    /// Free-form text describing the user's preferred coding style, naming conventions,
    /// error handling philosophy, testing requirements, and any other personal standards.
    /// Injected into every Compose/Transmute prompt as a top-level instruction.
    /// </summary>
    public string? UserEthos { get; set; }
}
