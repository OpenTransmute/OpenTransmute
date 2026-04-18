namespace OpenTransmute.Orchestrator.Contracts;

public enum OrchestratorType
{
    /// <summary>
    /// Invokes the claude CLI as a subprocess against the Anthropic API.
    /// Uses the user's existing credentials. Model weight maps to Opus / Sonnet / Haiku automatically.
    /// </summary>
    ClaudeCode,

    /// <summary>
    /// Calls a local Ollama instance via the OpenAI-compatible API (http://localhost:11434).
    /// Caller supplies thick/regular/thin model names.
    /// </summary>
    Ollama,

    /// <summary>
    /// Calls the OpenAI API (or a compatible endpoint) via Semantic Kernel.
    /// Caller supplies API key, optional custom endpoint, and thick/regular/thin model names.
    /// </summary>
    OpenAI
}
