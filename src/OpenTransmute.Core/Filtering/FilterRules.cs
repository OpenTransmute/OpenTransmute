namespace OpenTransmute.Filtering;

public static class FilterRules
{
    public static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "build", "out", "target",
        "bin", "obj", ".cache", "__pycache__", ".gradle", ".next",
        "vendor", "Pods", "DerivedData", ".vs", ".idea", "coverage",
        "packages", ".nuget"
    };

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

    public static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "Cargo.lock",
        "Gemfile.lock", "composer.lock", "poetry.lock", "Pipfile.lock"
    };

    public const long MaxFileSizeBytes = 500 * 1024; // 500 KB
}
