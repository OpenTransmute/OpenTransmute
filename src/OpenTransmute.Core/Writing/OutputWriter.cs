namespace OpenTransmute.Writing;

/// <summary>
/// Atomically writes phase output files to Output/Decomposition/&lt;project&gt;/&lt;filename&gt;.
/// Uses a tmp + move pattern so partial writes are never visible to readers.
/// </summary>
public sealed class OutputWriter
{
    #region Methods

    /// <summary>
    /// Writes <paramref name="content"/> to <c>Output/Decomposition/&lt;projectName&gt;/&lt;filename&gt;</c>
    /// under <paramref name="outputRoot"/> using an atomic tmp + rename.
    /// </summary>
    /// <param name="outputRoot">Root directory where Output/Decomposition/ lives.</param>
    /// <param name="projectName">Project subdirectory name.</param>
    /// <param name="filename">Output filename (e.g. "02-structural-survey.md").</param>
    /// <param name="content">File content to write.</param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    /// <returns>The absolute path of the written file.</returns>
    public async Task<string> WriteAsync(
        string outputRoot, string projectName, string filename, string content,
        CancellationToken ct = default)
    {
        string dir = Path.Combine(outputRoot, "Output", "Decomposition", projectName);
        Directory.CreateDirectory(dir);

        string finalPath = Path.Combine(dir, filename);
        string tmpPath   = finalPath + ".tmp";

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                $"OutputWriter refused to write an empty file: {filename}");

        await File.WriteAllTextAsync(tmpPath, content, new System.Text.UTF8Encoding(false), ct);
        File.Move(tmpPath, finalPath, overwrite: true);

        return finalPath;
    }

    /// <summary>
    /// Deletes all output files belonging to <paramref name="phaseNumber"/> (pattern
    /// <c>{phaseNumber:D2}-*.md</c>) from the project directory, plus any leftover .tmp files.
    /// Safe to call even if no files exist yet.
    /// </summary>
    public void DeletePhaseFiles(string outputRoot, string projectName, int phaseNumber)
    {
        string dir = Path.Combine(outputRoot, "Output", "Decomposition", projectName);
        if (!Directory.Exists(dir)) return;

        string prefix = $"{phaseNumber:D2}-";
        foreach (string file in Directory.GetFiles(dir, $"{prefix}*.md")
                     .Concat(Directory.GetFiles(dir, $"{prefix}*.tmp")))
        {
            File.Delete(file);
        }
    }

    /// <summary>Returns the expected absolute path for a phase output file without writing it.</summary>
    public string GetPath(string outputRoot, string projectName, string filename)
        => Path.Combine(outputRoot, "Output", "Decomposition", projectName, filename);

    /// <summary>Returns the Output/Decomposition project directory for the given output root and project name.</summary>
    public string GetProjectDirectory(string outputRoot, string projectName)
        => Path.Combine(outputRoot, "Output", "Decomposition", projectName);

    #endregion
}
