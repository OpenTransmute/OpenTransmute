namespace OpenTransmute.Writing;

/// <summary>
/// Atomically writes phase output files to Output/Decomposition/&lt;project&gt;/&lt;filename&gt;.
/// Uses a tmp + move pattern so partial writes are never visible to readers.
/// </summary>
public sealed class OutputWriter
{
    #region Methods

    /// <summary>
    /// Strips any lines before the first Markdown heading (<c># </c>) from the content.
    /// Catches model "thinking aloud" preamble that leaks into output (e.g.
    /// "Now let me look at..." or "I now have enough information...").
    /// Returns the content unchanged if no heading is found or the heading is already first.
    /// </summary>
    public static string StripPreamble(string content)
    {
        int idx = 0;
        while (idx < content.Length)
        {
            // Check if this line starts with '# ' (H1 heading)
            if (content[idx] == '#' && idx + 1 < content.Length && content[idx + 1] == ' ')
                return content[idx..];

            // Skip to next line
            int nl = content.IndexOf('\n', idx);
            if (nl < 0) break;
            idx = nl + 1;
        }

        // No heading found — return as-is rather than discarding everything
        return content;
    }

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

        content = StripPreamble(content);

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

    /// <summary>
    /// Deletes expansion output files for <paramref name="phaseNumber"/> at or above
    /// <paramref name="startItem"/> (1-based). Files below that index are preserved.
    /// Also preserves the discovery file (e.g. 03-00-discovery.json).
    /// </summary>
    public void DeletePhaseFilesFrom(string outputRoot, string projectName, int phaseNumber, int startItem)
    {
        string dir = Path.Combine(outputRoot, "Output", "Decomposition", projectName);
        if (!Directory.Exists(dir)) return;

        string prefix = $"{phaseNumber:D2}-";
        foreach (string file in Directory.GetFiles(dir, $"{prefix}*.md")
                     .Concat(Directory.GetFiles(dir, $"{prefix}*.tmp")))
        {
            string name = Path.GetFileName(file);
            // Preserve discovery file (03-00-discovery.json) — it starts with the phase prefix
            // but has index 00 which is below any valid startItem (1-based).
            if (name.Length >= prefix.Length + 2 &&
                int.TryParse(name.AsSpan(prefix.Length, 2), out int fileIndex) &&
                fileIndex >= startItem)
            {
                File.Delete(file);
            }
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
