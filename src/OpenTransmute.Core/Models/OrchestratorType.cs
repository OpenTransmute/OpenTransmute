namespace OpenTransmute.Models;

/// <summary>
/// Selects which LLM backend executes a job. Each value maps to a concrete
/// <c>ILlmExecutor</c> implementation resolved at run time.
/// </summary>
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
    /// Calls the OpenAI API (or a compatible endpoint) via Microsoft.Extensions.AI.
    /// Caller supplies API key, optional custom endpoint, and thick/regular/thin model names.
    /// </summary>
    OpenAI,

    /// <summary>
    /// Invokes the GitHub Copilot CLI (copilot) as a subprocess in non-interactive prompt mode.
    /// Uses the user's existing GitHub OAuth session or GH_TOKEN / GITHUB_TOKEN env var.
    /// Supports model selection via --model and tool control via --allow-tool / --deny-tool.
    /// </summary>
    CopilotCli
}
