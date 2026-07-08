namespace OpenTransmute.Models;

/// <summary>
/// Context-window tier for models that support an expanded ("long context") window on the
/// Copilot CLI path. This maps directly to the Copilot SDK's create-session
/// <c>context_tier</c> parameter (default | long_context) — see microsoft/conductor#251.
/// The tier is the ONLY supported way to unlock a model's large window (e.g. Claude Opus's
/// 1M); a numeric token override is clamped to the model's default (~200k) and ignored.
/// No effect on the Claude Code or OpenAI orchestrators.
/// </summary>
public enum LlmContextTier
{
    /// <summary>The model's default context window (Copilot CLI defaults to ~200k).</summary>
    Default,

    /// <summary>The model's expanded long-context window (e.g. 1M on Claude Opus).</summary>
    LongContext
}
