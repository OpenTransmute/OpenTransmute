using OpenTransmute.Filtering;
using OpenTransmute.Models;

namespace OpenTransmute.Exploration;

/// <summary>
/// Builds a deterministic, token-bounded structural digest of a repository for the mono-repo
/// segmentation pipeline (Option A). The digest is the input context for a later LLM clustering
/// step; building it first lets the structural summary be inspected before any model call.
/// </summary>
/// <remarks>
/// The digest's purpose is to make a huge tree clusterable. A folder holding 250 near-identical
/// tool subdirectories is summarized as one named group (all 250 names, one shared profile, no
/// subtrees) so the model groups them by role rather than emitting 250 separate segments.
/// </remarks>
public sealed class ProjectExplorer
{
    #region Members

    private readonly ILogger<ProjectExplorer> _logger;
    private readonly SourceFileFilter _filter;

    #endregion

    #region Constructor

    public ProjectExplorer(ILogger<ProjectExplorer> logger, SourceFileFilter filter)
    {
        _logger = logger;
        _filter = filter;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Builds the structural digest for <paramref name="rootPath"/>. Source files are enumerated
    /// through <see cref="SourceFileFilter"/> (the same filter the decompose pipeline uses) so the
    /// digest counts exactly what a decompose run would see.
    /// </summary>
    /// <param name="rootPath">Absolute or relative path to the repository root.</param>
    /// <param name="maxDepth">Directory depth to expand before summarizing stats only. Default 4.</param>
    /// <param name="collapseThreshold">
    /// Minimum number of structurally-identical sibling directories required to collapse them into a
    /// single <see cref="CollapsedGroup"/> instead of expanding each. Default 8.
    /// </param>
    /// <returns>A populated <see cref="RepoDigest"/>; never null.</returns>
    public RepoDigest BuildDigest(string rootPath, int maxDepth = 4, int collapseThreshold = 8)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        string fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"Repository root not found: {fullRoot}");

        _logger.LogInformation("BuildDigest: scanning {Root}", fullRoot);

        DirAgg root = new DirAgg { Name = new DirectoryInfo(fullRoot).Name, RelPath = string.Empty };
        long totalBytes = 0;
        int totalFiles = 0;

        // Single enumeration through the shared filter — the one source of truth for "what counts".
        // inspectContent:false skips the per-file binary sniff: opening 70k+ files just to check for
        // null bytes is the difference between a sub-second scan and a multi-minute one (AV taxes every
        // open). Extension/name/size exclusions still apply, which is all a structural digest needs.
        foreach ((string relPath, string absPath) in _filter.Apply(fullRoot, inspectContent: false))
        {
            long length;
            try { length = new FileInfo(absPath).Length; }
            catch (Exception ex)
            {
                _logger.LogWarning("BuildDigest: size read failed for {Path}: {Msg}", absPath, ex.Message);
                length = 0;
            }

            totalFiles++;
            totalBytes += length;

            string[] parts = relPath.Split('/');
            DirAgg cursor = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string seg = parts[i];
                if (!cursor.SubDirs.TryGetValue(seg, out DirAgg? child))
                {
                    child = new DirAgg
                    {
                        Name = seg,
                        RelPath = cursor.RelPath.Length == 0 ? seg : $"{cursor.RelPath}/{seg}"
                    };
                    cursor.SubDirs[seg] = child;
                }
                cursor = child;
            }

            string fileName = parts[^1];
            cursor.DirectFileCount++;
            cursor.DirectBytes += length;

            string ext = Path.GetExtension(fileName);
            ext = string.IsNullOrEmpty(ext) ? "(noext)" : ext.ToLowerInvariant();
            cursor.DirectExt[ext] = cursor.DirectExt.GetValueOrDefault(ext) + 1;

            if (MarkerKindFor(fileName) is SegmentKind marker)
                cursor.DirectMarkers[marker] = cursor.DirectMarkers.GetValueOrDefault(marker) + 1;
        }

        Aggregate(root);

        Dictionary<SegmentKind, int> markerCounts = new();
        CountMarkers(root, markerCounts);

        // Full per-directory stats, keyed by the same root-prefixed path the rendered digest shows the
        // model. Built from the complete pre-collapse tree so paths inside collapsed/truncated groups
        // still resolve when the model selects them.
        Dictionary<string, PathStat> pathStats = new(StringComparer.OrdinalIgnoreCase);
        CollectPathStats(root, root.Name, pathStats);

        DigestNode rootNode = ToDigestNode(root, depth: 0, maxDepth, collapseThreshold);

        _logger.LogInformation("BuildDigest: {Files} files, {Bytes} bytes, {Markers} marker kinds",
            totalFiles, totalBytes, markerCounts.Count);

        return new RepoDigest
        {
            RootPath = fullRoot,
            ProjectName = root.Name,
            GeneratedAtUtc = DateTime.UtcNow,
            TotalFileCount = totalFiles,
            TotalBytes = totalBytes,
            MarkerCounts = markerCounts,
            PathStats = pathStats,
            Root = rootNode
        };
    }

    /// <summary>
    /// Walks the full aggregated tree, recording each directory's subtree stats under the root-prefixed
    /// path the digest renders (e.g. <c>SC2/Tools/Source</c>). The root itself is keyed by its bare name.
    /// </summary>
    private static void CollectPathStats(DirAgg node, string rootName, Dictionary<string, PathStat> map)
    {
        string key = node.RelPath.Length == 0 ? rootName : $"{rootName}/{node.RelPath}";
        map[key] = new PathStat(node.SubtreeFileCount, node.SubtreeBytes);
        foreach (DirAgg child in node.SubDirs.Values)
            CollectPathStats(child, rootName, map);
    }

    /// <summary>Post-order pass computing subtree file counts, bytes, extension histograms, and marker kinds.</summary>
    private static void Aggregate(DirAgg node)
    {
        node.SubtreeFileCount = node.DirectFileCount;
        node.SubtreeBytes = node.DirectBytes;

        foreach (KeyValuePair<string, int> kv in node.DirectExt)
            node.SubtreeExt[kv.Key] = node.SubtreeExt.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (SegmentKind m in node.DirectMarkers.Keys)
            node.SubtreeMarkers.Add(m);

        foreach (DirAgg child in node.SubDirs.Values)
        {
            Aggregate(child);
            node.SubtreeFileCount += child.SubtreeFileCount;
            node.SubtreeBytes += child.SubtreeBytes;
            foreach (KeyValuePair<string, int> kv in child.SubtreeExt)
                node.SubtreeExt[kv.Key] = node.SubtreeExt.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (SegmentKind m in child.SubtreeMarkers)
                node.SubtreeMarkers.Add(m);
        }
    }

    /// <summary>Sums marker-file counts per ecosystem across the whole tree.</summary>
    private static void CountMarkers(DirAgg node, Dictionary<SegmentKind, int> counts)
    {
        foreach (KeyValuePair<SegmentKind, int> kv in node.DirectMarkers)
            counts[kv.Key] = counts.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (DirAgg child in node.SubDirs.Values)
            CountMarkers(child, counts);
    }

    /// <summary>
    /// Converts the aggregated tree into the rendered <see cref="DigestNode"/> form, collapsing
    /// homogeneous sibling sets and truncating below <paramref name="maxDepth"/>.
    /// </summary>
    private DigestNode ToDigestNode(DirAgg node, int depth, int maxDepth, int collapseThreshold)
    {
        DigestNode dn = new DigestNode
        {
            Name = node.Name,
            RelativePath = node.RelPath,
            SubtreeFileCount = node.SubtreeFileCount,
            SubtreeBytes = node.SubtreeBytes,
            TopExtensions = TopExt(node.SubtreeExt, 3),
            Markers = node.DirectMarkers.Keys.OrderBy(m => m.ToString()).ToList()
        };

        List<DirAgg> children = node.SubDirs.Values.ToList();
        if (children.Count == 0)
            return dn;

        // Depth cap: report that subdirs exist but stop expanding to keep the digest bounded.
        if (depth >= maxDepth)
        {
            dn.Truncated = true;
            return dn;
        }

        // Homogeneity collapse: group siblings by structural signature; any group at or above the
        // threshold becomes a single CollapsedGroup (names only), the rest expand recursively.
        foreach (IGrouping<string, DirAgg> group in children.GroupBy(Signature))
        {
            List<DirAgg> members = group.ToList();
            if (members.Count >= collapseThreshold)
            {
                dn.Collapsed.Add(new CollapsedGroup
                {
                    Profile = ProfileLabel(members[0]),
                    Names = members.Select(m => m.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                    Count = members.Count,
                    SubtreeFileCount = members.Sum(m => m.SubtreeFileCount),
                    SubtreeBytes = members.Sum(m => m.SubtreeBytes)
                });
            }
            else
            {
                foreach (DirAgg m in members)
                    dn.Children.Add(ToDigestNode(m, depth + 1, maxDepth, collapseThreshold));
            }
        }

        dn.Children = dn.Children.OrderByDescending(c => c.SubtreeBytes).ToList();
        dn.Collapsed = dn.Collapsed.OrderByDescending(c => c.SubtreeBytes).ToList();
        return dn;
    }

    /// <summary>
    /// Structural signature used to decide whether sibling directories are interchangeable: their
    /// top two subtree extensions plus the set of ecosystem markers present. Identical signature =
    /// collapsible together.
    /// </summary>
    private static string Signature(DirAgg d)
    {
        string exts = string.Join(",", TopExt(d.SubtreeExt, 2).Select(e => e.Ext));
        string markers = string.Join(",", d.SubtreeMarkers.OrderBy(m => m.ToString()));
        return $"{exts}|{markers}";
    }

    /// <summary>Human-readable profile label for a collapsed group, e.g. <c>[.py 250, .md 250] {Npm}</c>.</summary>
    private static string ProfileLabel(DirAgg d)
    {
        string exts = string.Join(", ", TopExt(d.SubtreeExt, 3).Select(e => $"{e.Ext} {e.Count}"));
        string markers = d.SubtreeMarkers.Count > 0
            ? " {" + string.Join(",", d.SubtreeMarkers.OrderBy(m => m.ToString())) + "}"
            : string.Empty;
        return $"[{exts}]{markers}";
    }

    /// <summary>Returns the top <paramref name="n"/> extensions by count, most frequent first.</summary>
    private static List<ExtensionCount> TopExt(Dictionary<string, int> ext, int n) =>
        ext.OrderByDescending(k => k.Value)
           .ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
           .Take(n)
           .Select(k => new ExtensionCount(k.Key, k.Value))
           .ToList();

    /// <summary>Maps a filename to its ecosystem marker kind, or null when it is not a marker.</summary>
    private static SegmentKind? MarkerKindFor(string fileName)
    {
        if (fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Npm;
        if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Dotnet;
        if (fileName.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Cargo;
        if (fileName.Equals("go.mod", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("go.work", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Go;
        if (fileName.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Maven;
        if (fileName.StartsWith("build.gradle", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("settings.gradle", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Gradle;
        if (fileName.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("setup.py", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("setup.cfg", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Python;
        if (fileName.Equals("composer.json", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Php;
        if (fileName.EndsWith(".gemspec", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Gemfile", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Ruby;
        return null;
    }

    #endregion

    #region Nested types

    /// <summary>Mutable per-directory accumulator used while building the digest tree.</summary>
    private sealed class DirAgg
    {
        public string Name { get; init; } = string.Empty;
        public string RelPath { get; init; } = string.Empty;

        public Dictionary<string, DirAgg> SubDirs { get; } = new(StringComparer.Ordinal);

        public int DirectFileCount { get; set; }
        public long DirectBytes { get; set; }
        public Dictionary<string, int> DirectExt { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<SegmentKind, int> DirectMarkers { get; } = new();

        public int SubtreeFileCount { get; set; }
        public long SubtreeBytes { get; set; }
        public Dictionary<string, int> SubtreeExt { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<SegmentKind> SubtreeMarkers { get; } = new();
    }

    #endregion
}
