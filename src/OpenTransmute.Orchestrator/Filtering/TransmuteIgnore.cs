namespace OpenTransmute.Orchestrator.Filtering;

/// <summary>
/// Parses a .transmuteignore file and determines whether files/directories
/// should be excluded from ingestion. Supports gitignore-style patterns:
/// <list type="bullet">
///   <item># lines are comments</item>
///   <item>Trailing / means directory-only</item>
///   <item>Leading / anchors the pattern to the root</item>
///   <item>** matches zero or more path segments</item>
///   <item>* matches any characters within a single segment</item>
///   <item>! negates a pattern</item>
/// </list>
/// </summary>
public sealed class TransmuteIgnore
{
    public static readonly TransmuteIgnore Empty = new();

    private readonly List<IgnorePattern> _patterns = new();

    private TransmuteIgnore() { }

    /// <summary>
    /// Loads .transmuteignore from <paramref name="rootPath"/>.
    /// Returns <see cref="Empty"/> if no file is found.
    /// </summary>
    public static TransmuteIgnore Load(string ignoreContents)
    {
        var instance = new TransmuteIgnore();
        foreach (string rawLine in ignoreContents.Split("\n"))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            bool negate = line.StartsWith('!');
            if (negate) line = line[1..].Trim();

            bool dirOnly = line.EndsWith('/');
            if (dirOnly) line = line.TrimEnd('/');

            if (!string.IsNullOrEmpty(line))
                instance._patterns.Add(new IgnorePattern(line, negate, dirOnly));
        }
        return instance;
    }

    /// <summary>
    /// Returns true if the path should be excluded from ingestion.
    /// </summary>
    /// <param name="relativePath">Path relative to root using forward slashes.</param>
    /// <param name="isDirectory">Whether the path is a directory.</param>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        relativePath = relativePath.Replace('\\', '/').Trim('/');
        bool ignored = false;
        foreach (var p in _patterns)
        {
            if (p.DirOnly && !isDirectory) continue;
            if (p.Matches(relativePath))
                ignored = !p.Negate;
        }
        return ignored;
    }

    // -------------------------------------------------------------------------

    private sealed class IgnorePattern
    {
        private readonly string _pattern;
        private readonly bool _anchored; // pattern contains a non-trailing slash
        public bool Negate  { get; }
        public bool DirOnly { get; }

        public IgnorePattern(string pattern, bool negate, bool dirOnly)
        {
            Negate  = negate;
            DirOnly = dirOnly;
            _anchored = pattern.Contains('/');
            _pattern  = pattern.TrimStart('/');
        }

        public bool Matches(string relativePath)
        {
            if (_anchored)
                return GlobMatch(_pattern, relativePath);

            // No-slash patterns match against every individual segment (gitignore spec).
            foreach (string segment in relativePath.Split('/'))
                if (SegmentMatch(_pattern, segment)) return true;
            return false;
        }
    }

    // ---- glob engine --------------------------------------------------------

    private static bool GlobMatch(string pattern, string path)
        => MatchSegments(pattern.Split('/'), 0, path.Split('/'), 0);

    private static bool MatchSegments(string[] pat, int pi, string[] seg, int si)
    {
        while (pi < pat.Length)
        {
            if (pat[pi] == "**")
            {
                for (int k = si; k <= seg.Length; k++)
                    if (MatchSegments(pat, pi + 1, seg, k)) return true;
                return false;
            }
            if (si >= seg.Length) return false;
            if (!SegmentMatch(pat[pi], seg[si])) return false;
            pi++; si++;
        }
        return si == seg.Length;
    }

    private static bool SegmentMatch(string pattern, string input)
        => WildcardMatch(pattern, 0, input, 0);

    private static bool WildcardMatch(string p, int pi, string s, int si)
    {
        while (pi < p.Length)
        {
            if (p[pi] == '*')
            {
                while (pi < p.Length && p[pi] == '*') pi++;
                if (pi == p.Length) return true;
                for (int k = si; k <= s.Length; k++)
                    if (WildcardMatch(p, pi, s, k)) return true;
                return false;
            }
            if (si >= s.Length) return false;
            if (p[pi] != '?' && !CharMatch(p[pi], s[si])) return false;
            pi++; si++;
        }
        return si == s.Length;
    }

    private static bool CharMatch(char a, char b)
        => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
