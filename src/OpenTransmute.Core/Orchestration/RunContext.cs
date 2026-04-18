using OpenTransmute.Models;

namespace OpenTransmute.Orchestration;

/// <summary>
/// Mutable state that accumulates during a single decompose run.
/// Passed through every phase so each can reference prior outputs and append token totals.
/// </summary>
public sealed class RunContext
{
    #region Constructor

    public RunContext(string projectName, string sourcePath, string outputRoot, string? hints = null)
    {
        ProjectName = projectName;
        SourcePath  = sourcePath;
        OutputRoot  = outputRoot;
        Hints       = hints;
    }

    #endregion

    #region Properties

    public string ProjectName { get; }
    public string SourcePath  { get; }
    public string OutputRoot  { get; }

    /// <summary>Optional user-supplied hints injected into every phase prompt.</summary>
    public string? Hints { get; }

    /// <summary>
    /// filename (e.g. "00-index.md") → absolute path on disk.
    /// Populated after each phase completes. Used by PromptBuilder to tell the agent
    /// which prior files to read before responding.
    /// </summary>
    public Dictionary<string, string> PriorOutputPaths { get; } = new();

    /// <summary>Accumulated token usage across all phases completed so far.</summary>
    public TokenAccumulator Tokens { get; } = new();

    /// <summary>Set to true by an orchestrator when a phase fails, signalling the run loop to stop.</summary>
    public bool LastFailed { get; set; }

    #endregion

    #region Methods

    /// <summary>
    /// Returns a point-in-time copy of PriorOutputPaths.
    /// Used by expansion phases to capture the pre-loop baseline so that all expansion
    /// items receive identical prior context and do not accumulate each other's outputs.
    /// </summary>
    public IReadOnlyDictionary<string, string> SnapshotPriorPaths()
        => new Dictionary<string, string>(PriorOutputPaths);

    /// <summary>
    /// Seeds prior outputs from files already on disk when resuming a run (StartPhase &gt; 0).
    /// Scans Output/Decomposition/&lt;project&gt;/ for 00-*.md, 01-*.md … &lt;upToPhase-1&gt;-*.md.
    /// </summary>
    public void LoadPriorOutputs(int upToPhase)
    {
        string dir = Path.Combine(OutputRoot, "Output", "Decomposition", ProjectName);
        if (!Directory.Exists(dir)) return;

        for (int phase = 0; phase < upToPhase; phase++)
        {
            foreach (string file in Directory.GetFiles(dir, $"{phase:D2}-*.md").OrderBy(f => f))
                PriorOutputPaths[Path.GetFileName(file)] = file;
        }
    }

    #endregion
}
