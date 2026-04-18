using System.Buffers;
using OpenTransmute.Filtering;

namespace OpenTransmute.Filtering;

/// <summary>
/// Used by API-route backends to assemble a file block.
/// ClaudeAgentBackend skips this — the agent reads files using its own tools.
/// Applies FilterRules to exclude noise (binaries, lock files, oversized files, etc.).
/// </summary>
public class SourceFileFilter
{
    #region Members

    private readonly ILogger<SourceFileFilter> _logger;

    #endregion

    #region Constructor

    public SourceFileFilter(ILogger<SourceFileFilter> logger)
    {
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Enumerates all source files under <paramref name="rootPath"/> that pass the filter rules.
    /// Returns (relativePath, absolutePath) tuples for each file.
    /// </summary>
    /// <param name="rootPath">The root directory to enumerate from.</param>
    /// <returns>Pairs of (relative path with forward slashes, absolute path) for each included file.</returns>
    public IEnumerable<(string RelativePath, string AbsolutePath)> Apply(string rootPath)
    {
        TransmuteIgnore ignore = TransmuteIgnore.Load(rootPath);
        return EnumerateFiles(rootPath, rootPath, ignore);
    }

    private IEnumerable<(string, string)> EnumerateFiles(string root, string current, TransmuteIgnore ignore)
    {
        // Skip excluded directories
        foreach (string dir in Directory.GetDirectories(current))
        {
            string dirName = new DirectoryInfo(dir).Name;
            if (FilterRules.ExcludedDirectories.Contains(dirName)) continue;

            string rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
            if (ignore.IsIgnored(rel, isDirectory: true)) continue;

            foreach (var f in EnumerateFiles(root, dir, ignore))
                yield return f;
        }

        foreach (string file in Directory.GetFiles(current))
        {
            FileInfo info = new FileInfo(file);

            if (FilterRules.ExcludedFileNames.Contains(info.Name)) continue;
            if (FilterRules.ExcludedExtensions.Contains(info.Extension)) continue;
            if (info.Length > FilterRules.MaxFileSizeBytes) continue;
            if (IsBinary(file)) continue;

            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (ignore.IsIgnored(rel, isDirectory: false)) continue;

            yield return (rel, file);
        }
    }

    private bool IsBinary(string path)
    {
        try
        {
            using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return false;

            int readLen = (int)Math.Min(8192, fs.Length);

            // Rent from the pool to avoid repeated small heap allocations across a large file tree.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(readLen);
            try
            {
                int read = fs.Read(buffer, 0, readLen);
                return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SourceFileFilter: cannot read {Path} for binary check — treating as binary", path);
            return true;
        }
    }

    #endregion
}
