namespace OpenTransmute.Filtering;

/// <summary>
/// Static, hard-coded exclusion rules for source scanning. Filters out the noise that has no business
/// in a decomposition — dependency caches, build output, binaries, media, and oversized files — so the
/// orchestrator only ever sees actual source. These are the baseline rules; per-project
/// <c>.transmuteignore</c> patterns layer on top.
/// </summary>
public static class FilterRules
{
    /// <summary>Directory names skipped wholesale (dependency caches, build output, IDE metadata).</summary>
    public static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "build", "out", "target",
        "bin", "obj", ".cache", "__pycache__", ".gradle", ".next",
        "vendor", "Pods", "DerivedData", ".vs", ".idea", "coverage",
        "packages", ".nuget"
    };

    /// <summary>File extensions excluded as non-source (binaries, media, minified assets, lock files).</summary>
    public static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Locks
        ".lock",
        // Minified
        ".min.js", ".min.css",
        // Source maps
        ".map",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".bmp", ".tiff",
        // Fonts
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        // Documents
        ".pdf", ".docx", ".xlsx", ".pptx",
        // Archives
        ".zip", ".gz", ".tar", ".rar", ".7z",
        // Binaries
        ".exe", ".dll", ".so", ".dylib", ".o", ".a", ".lib", ".pdb",
        // Compiled
        ".pyc", ".class", ".jar", ".war",
        // DB
        ".db", ".sqlite", ".sqlite3",
        // Media
        ".mp3", ".mp4", ".wav", ".avi", ".mov"
    };

    /// <summary>Specific file names excluded regardless of extension (dependency lock files).</summary>
    public static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "Cargo.lock",
        "Gemfile.lock", "composer.lock", "poetry.lock", "Pipfile.lock"
    };

    /// <summary>Upper size bound (bytes) for an individual source file; anything larger is skipped.</summary>
    public const long MaxFileSizeBytes = 500 * 1024; // 500 KB
}
