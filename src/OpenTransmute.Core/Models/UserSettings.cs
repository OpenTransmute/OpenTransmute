namespace OpenTransmute.Models;

/// <summary>
/// Persisted user preferences. A single row (singleton) is maintained in the database.
/// The API key is intentionally excluded — it is held in memory only and never written to disk.
/// </summary>
public class UserSettings
{
    /// <summary>Fixed ID used for the singleton settings row.</summary>
    public static readonly Guid SingletonId = new Guid("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    // ── Engine ───────────────────────────────────────────────────────────────
    /// <summary>Stored as int to avoid a hard dependency on the Orchestrator enum in migrations.</summary>
    public int Orchestrator { get; set; } = 2; // OrchestratorType.OpenAI

    // ── OpenAI / compatible endpoint ─────────────────────────────────────────
    public string? OpenAiEndpoint { get; set; }

    // ── Model tiers (Decompose) ───────────────────────────────────────────────
    public string? ThickModel   { get; set; }
    public string? RegularModel { get; set; }
    public string? ThinModel    { get; set; }

    // ── Run defaults ─────────────────────────────────────────────────────────
    public int MaxTurns               { get; set; } = 20;
    public int MaxOutputTokens        { get; set; } = 0;
    public int TimeoutMinutes         { get; set; } = 10;
    public int ThickMaxOutputTokens   { get; set; } = 32728;
    public int RegularMaxOutputTokens { get; set; } = 16384;
    public int ThinMaxOutputTokens    { get; set; } = 8192;

    /// <summary>Context-window tier for the Copilot CLI path. Default = ~200k; LongContext = the model's expanded window.</summary>
    public LlmContextTier ContextTier { get; set; } = LlmContextTier.Default;

    // ── Personal composition ethos ────────────────────────────────────────────
    /// <summary>
    /// Free-form text describing the user's preferred coding style, naming conventions,
    /// error handling philosophy, testing requirements, and any other personal standards.
    /// Injected into every Compose/Transmute prompt as a top-level instruction.
    /// </summary>
    public string? UserEthos { get; set; }
}
