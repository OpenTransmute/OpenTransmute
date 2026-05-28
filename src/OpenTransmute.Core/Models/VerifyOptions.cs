using System.Text.Json.Serialization;

namespace OpenTransmute.Models;

/// <summary>
/// All user-facing inputs for a verify run. Identifies the decomposition output
/// directory and the original source code path so each document can be audited
/// against the actual code.
/// </summary>
public class VerifyOptions
{
    #region Properties

    /// <summary>Project name — must match the decomposition output directory name.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to the original source code. The model reads files here to verify
    /// claims in each decomposition document.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Which orchestrator engine to use.</summary>
    public OrchestratorType Orchestrator { get; set; } = OrchestratorType.ClaudeCode;

    /// <summary>Model name passed to the backend. Null = backend default.</summary>
    public string? Model { get; set; }

    /// <summary>Optional custom base URL for OpenAI-compatible endpoints.</summary>
    public string? OpenAiEndpoint { get; set; }

    /// <summary>OpenAI API key. Required when Orchestrator == OpenAI. Never persisted to disk.</summary>
    [JsonIgnore]
    public string? OpenAiApiKey { get; set; }

    /// <summary>Root directory where Output/Decomposition/&lt;ProjectName&gt;/ lives.</summary>
    public string OutputRoot { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>
    /// Output token ceiling passed to the backend. Verify reports can be large — default is generous.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 32768;

    /// <summary>Maximum agentic tool-use rounds per document before the model must produce output.</summary>
    public int MaxTurns { get; set; } = 30;

    /// <summary>HTTP timeout in minutes for direct completion backends (OpenAI/Ollama).</summary>
    public int TimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Optional free-text hints injected into the verify prompt. Use to focus the audit
    /// on specific concerns or known problem areas.
    /// </summary>
    public string? Hints { get; set; }

    /// <summary>
    /// When set, only verify these specific document filenames (e.g. "01-structural-survey.md").
    /// Null or empty means verify all documents in the decomposition output directory.
    /// </summary>
    public List<string>? DocumentFilter { get; set; }

    /// <summary>
    /// When true, skip per-document verification and re-run only the rollup summary
    /// and remediation pass using existing verification reports on disk.
    /// </summary>
    public bool SummaryOnly { get; set; }

    #endregion
}
