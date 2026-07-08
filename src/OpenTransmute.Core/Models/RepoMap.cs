using System.Text;

namespace OpenTransmute.Models;

/// <summary>
/// One logical segment of a mono-repo as proposed by the LLM clustering step: a named group of one
/// or more directories that share a role/subsystem and can be decomposed independently of the rest.
/// </summary>
/// <remarks>
/// <see cref="FileCount"/> and <see cref="Bytes"/> are resolved after the model responds by matching
/// the model's <see cref="Paths"/> back against the digest tree. Sizing is a strict disjoint partition:
/// every file is attributed to exactly one segment (the most specific claiming path wins), so the same
/// bytes are never counted in two segments even when one segment's directory nests inside another's.
/// </remarks>
public sealed class RepoSegment
{
    #region Properties

    /// <summary>Short human-readable segment name (e.g. "Localization Toolchain").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>One- or two-word role/subsystem label the model assigned (e.g. "build-tooling", "vendored").</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Repository-relative directory paths assigned to this segment, as drawn from the digest.</summary>
    public IReadOnlyList<string> Paths { get; init; } = new List<string>();

    /// <summary>The model's one-sentence justification for grouping these paths together.</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Disjoint total source files attributed to this segment (0 when a path could not be matched).</summary>
    public int FileCount { get; set; }

    /// <summary>Disjoint total source bytes attributed to this segment.</summary>
    public long Bytes { get; set; }

    #endregion
}

/// <summary>
/// The result of the LLM clustering step: the full set of <see cref="RepoSegment"/>s the model carved
/// the repository into, plus the digest stats they were derived from. This is the hand-off artifact
/// for the eventual per-segment decompose runs.
/// </summary>
public sealed class RepoMap
{
    #region Properties

    /// <summary>Absolute path of the repository root.</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>Repository name (root directory name).</summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>UTC timestamp the map was produced.</summary>
    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>The size-banded group-count guidance handed to the model (e.g. "10-15 groups").</summary>
    public string TargetGuidance { get; init; } = string.Empty;

    /// <summary>The segments the model proposed.</summary>
    public IReadOnlyList<RepoSegment> Segments { get; init; } = new List<RepoSegment>();

    /// <summary>
    /// Diagnostic notes for overlaps the deterministic post-pass had to resolve: exact paths the model
    /// listed in more than one segment, and parent/child paths split across segments. Each note records
    /// which segment kept the contested files. Empty when the model produced a clean partition.
    /// </summary>
    public IReadOnlyList<string> Overlaps { get; init; } = new List<string>();

    /// <summary>Raw model response text, retained for debugging the parse when something looks off.</summary>
    public string RawResponse { get; init; } = string.Empty;

    #endregion

    #region Methods

    /// <summary>Renders the map as an inspectable text report: one block per segment with stats, rationale, and paths.</summary>
    public string Render()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== Repo Map: {ProjectName} ===");
        sb.AppendLine($"Root: {RootPath}");
        sb.AppendLine($"Generated: {GeneratedAtUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"Segments: {Segments.Count} (target {TargetGuidance})");

        int totalFiles = Segments.Sum(s => s.FileCount);
        long totalBytes = Segments.Sum(s => s.Bytes);
        sb.AppendLine($"Resolved coverage: {totalFiles:N0} files, {FormatBytes(totalBytes)}");
        sb.AppendLine();

        int index = 1;
        foreach (RepoSegment seg in Segments.OrderByDescending(s => s.Bytes))
        {
            sb.AppendLine($"[{index}] {seg.Name}  ({seg.Role})  \u2014 {seg.FileCount:N0} files, {FormatBytes(seg.Bytes)}");
            if (!string.IsNullOrWhiteSpace(seg.Rationale))
                sb.AppendLine($"     {seg.Rationale}");
            foreach (string path in seg.Paths)
                sb.AppendLine($"       - {path}");
            sb.AppendLine();
            index++;
        }

        if (Overlaps.Count > 0)
        {
            sb.AppendLine($"--- Overlaps resolved ({Overlaps.Count}) ---");
            foreach (string note in Overlaps)
                sb.AppendLine($"  {note}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Formats a byte count as a human-readable size (B/KB/MB/GB).</summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:N1} {units[unit]}";
    }

    #endregion
}
