using System.Text;

namespace OpenTransmute.Models;

/// <summary>
/// A token-bounded structural summary of a repository, built deterministically (no LLM) and intended
/// as the input context for the LLM clustering step. The defining feature is <em>homogeneity
/// collapse</em>: large sets of structurally-similar sibling directories (e.g. 250 single-tool
/// folders) are summarized as a named group rather than expanded, so the model can cluster them by
/// role instead of treating each as its own segment.
/// </summary>
public sealed class RepoDigest
{
    #region Properties

    /// <summary>Absolute path of the repository root the digest was built from.</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>Repository name (the root directory's name).</summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>UTC timestamp the digest was generated.</summary>
    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>Total source files counted across the tree (after <c>FilterRules</c> exclusions).</summary>
    public int TotalFileCount { get; init; }

    /// <summary>Total bytes of source across the tree.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Count of marker files found per ecosystem, aggregated across the whole tree.</summary>
    public IReadOnlyDictionary<SegmentKind, int> MarkerCounts { get; init; } =
        new Dictionary<SegmentKind, int>();

    /// <summary>Root node of the collapsed directory tree.</summary>
    public DigestNode Root { get; init; } = new();

    /// <summary>
    /// Subtree stats for <em>every</em> directory in the full tree, keyed by the same path string the
    /// rendered digest shows the model (root-name prefixed, forward slashes — e.g. <c>SC2/Tools/Source</c>).
    /// This covers directories that the rendered tree collapses or truncates away, so paths the model
    /// picks out of a collapsed group still resolve to real file counts and byte sizes. Built from the
    /// pre-collapse aggregation, not the rendered <see cref="Root"/> tree.
    /// </summary>
    public IReadOnlyDictionary<string, PathStat> PathStats { get; init; } =
        new Dictionary<string, PathStat>(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Methods

    /// <summary>
    /// Renders the digest as the indented text outline handed to the LLM. Collapsed sibling sets are
    /// shown as a single group line followed by the full list of their names (names are cheap; the
    /// model needs all of them to cluster, but it does not need their subtrees).
    /// </summary>
    public string Render()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== Repository Digest: {ProjectName} ===");
        sb.AppendLine($"Root: {RootPath}");
        sb.AppendLine($"Generated: {GeneratedAtUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"Total source: {TotalFileCount:N0} files, {FormatBytes(TotalBytes)}");

        if (MarkerCounts.Count > 0)
        {
            string markers = string.Join(", ", MarkerCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}\u00d7{kv.Value}"));
            sb.AppendLine($"Markers: {markers}");
        }

        sb.AppendLine();
        sb.AppendLine($"{ProjectName}/");
        RenderChildren(sb, Root, indent: "");
        return sb.ToString();
    }

    /// <summary>Recursively renders a node's expanded children and collapsed groups.</summary>
    private static void RenderChildren(StringBuilder sb, DigestNode node, string indent)
    {
        // Expanded children first, then collapsed groups — both ordered by size for readability.
        List<object> rows = new List<object>();
        rows.AddRange(node.Children);
        rows.AddRange(node.Collapsed);

        for (int i = 0; i < rows.Count; i++)
        {
            bool isLast = i == rows.Count - 1;
            string connector = isLast ? "\u2514\u2500\u2500 " : "\u251c\u2500\u2500 ";
            string childIndent = indent + (isLast ? "    " : "\u2502   ");

            if (rows[i] is DigestNode child)
            {
                string exts = child.TopExtensions.Count > 0
                    ? "  [" + string.Join(", ", child.TopExtensions.Select(e => $"{e.Ext} {e.Count}")) + "]"
                    : string.Empty;
                string markers = child.Markers.Count > 0
                    ? " {" + string.Join(",", child.Markers) + "}"
                    : string.Empty;
                string trunc = child.Truncated ? " \u2026(subdirs not expanded)" : string.Empty;

                sb.AppendLine($"{indent}{connector}{child.Name}/  \u2014 " +
                    $"{child.SubtreeFileCount:N0} files, {FormatBytes(child.SubtreeBytes)}{exts}{markers}{trunc}");
                RenderChildren(sb, child, childIndent);
            }
            else if (rows[i] is CollapsedGroup group)
            {
                sb.AppendLine($"{indent}{connector}\u25a3 {group.Count} similar subdirs {group.Profile} \u2014 " +
                    $"{group.SubtreeFileCount:N0} files, {FormatBytes(group.SubtreeBytes)}");
                sb.AppendLine($"{childIndent}names: {string.Join(", ", group.Names)}");
            }
        }
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

/// <summary>A single extension and its occurrence count within a directory subtree.</summary>
public sealed record ExtensionCount(string Ext, int Count);

/// <summary>Subtree file count and byte size for one directory, used to size the resolved segments.</summary>
public sealed record PathStat(int FileCount, long Bytes);

/// <summary>
/// One directory in the digest tree. Carries subtree aggregates (file count, bytes, dominant
/// extensions, ecosystem markers) plus either expanded <see cref="Children"/> or — when a set of
/// siblings is homogeneous — a <see cref="CollapsedGroup"/> in the parent's <see cref="Collapsed"/>.
/// </summary>
public sealed class DigestNode
{
    /// <summary>Directory name (leaf segment, no path).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Path relative to the repository root, forward-slashed.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Total source files in this directory's subtree.</summary>
    public int SubtreeFileCount { get; set; }

    /// <summary>Total source bytes in this directory's subtree.</summary>
    public long SubtreeBytes { get; set; }

    /// <summary>Dominant file extensions in the subtree, most frequent first.</summary>
    public IReadOnlyList<ExtensionCount> TopExtensions { get; init; } = new List<ExtensionCount>();

    /// <summary>Ecosystem markers found directly in this directory (e.g. a <c>package.json</c>).</summary>
    public IReadOnlyList<SegmentKind> Markers { get; init; } = new List<SegmentKind>();

    /// <summary>Expanded child directories (those not part of a collapsed homogeneous set).</summary>
    public List<DigestNode> Children { get; set; } = new();

    /// <summary>Homogeneous sibling sets collapsed under this directory.</summary>
    public List<CollapsedGroup> Collapsed { get; set; } = new();

    /// <summary>True when children were omitted because the depth cap was reached.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// A set of structurally-similar sibling directories summarized as one entry. Holds every member's
/// name (so the LLM can cluster them) and the aggregate size, but not their subtrees.
/// </summary>
public sealed class CollapsedGroup
{
    /// <summary>Shared structural profile, e.g. <c>[.py 250, .md 250]</c>.</summary>
    public string Profile { get; init; } = string.Empty;

    /// <summary>Names of every directory in the group.</summary>
    public IReadOnlyList<string> Names { get; init; } = new List<string>();

    /// <summary>Number of directories collapsed.</summary>
    public int Count { get; init; }

    /// <summary>Total source files across all collapsed members.</summary>
    public int SubtreeFileCount { get; init; }

    /// <summary>Total source bytes across all collapsed members.</summary>
    public long SubtreeBytes { get; init; }
}
