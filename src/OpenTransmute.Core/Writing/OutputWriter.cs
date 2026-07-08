namespace OpenTransmute.Writing;

/// <summary>
/// Atomically writes phase output files to Output/Decomposition/&lt;project&gt;/&lt;filename&gt;.
/// Uses a tmp + move pattern so partial writes are never visible to readers.
/// </summary>
public sealed class OutputWriter
{
    #region Methods

    /// <summary>
    /// Strips leading non-document preamble — the model's "thinking aloud" narration that
    /// precedes the actual deliverable (e.g. "Now let me look at..." / "I now have enough
    /// information...") — by skipping everything before the first Markdown ATX heading
    /// (<c># </c> … <c>###### </c>) that appears at the START of a line and OUTSIDE any fenced
    /// code block. Fence-awareness is the whole point: a YAML/shell comment like <c># Schema</c>
    /// inside a <c>```</c> block must never be mistaken for the document start — that exact bug
    /// truncated ~40% off a phase output once. Returns the content unchanged when no qualifying
    /// heading is found, rather than discarding everything.
    /// </summary>
    public static string StripPreamble(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        bool inFence = false;
        int idx = 0;
        while (idx < content.Length)
        {
            int nl = content.IndexOf('\n', idx);
            int lineEnd = nl < 0 ? content.Length : nl;

            // A fenced-code delimiter (``` or ~~~) toggles fence state. Headings and
            // comments inside a fence are content, not the document start.
            if (IsFenceLine(content, idx, lineEnd))
                inFence = !inFence;
            else if (!inFence && IsAtxHeadingLine(content, idx, lineEnd))
                return content[idx..];

            if (nl < 0) break;
            idx = nl + 1;
        }

        // No heading found — return as-is rather than discarding everything.
        return content;
    }

    /// <summary>True when line <c>[start, end)</c> is a fenced-code delimiter (≥3 backticks or tildes), ignoring leading whitespace.</summary>
    private static bool IsFenceLine(string s, int start, int end)
    {
        int i = start;
        while (i < end && (s[i] == ' ' || s[i] == '\t')) i++;
        if (i >= end || (s[i] != '`' && s[i] != '~')) return false;

        char marker = s[i];
        int run = 0;
        while (i < end && s[i] == marker) { i++; run++; }
        return run >= 3;
    }

    /// <summary>True when line <c>[start, end)</c> begins with 1–6 '#' followed by a space — a Markdown ATX heading.</summary>
    private static bool IsAtxHeadingLine(string s, int start, int end)
    {
        int i = start;
        int hashes = 0;
        while (i < end && s[i] == '#') { i++; hashes++; }
        return hashes >= 1 && hashes <= 6 && i < end && s[i] == ' ';
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
