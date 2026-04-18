using System.ComponentModel;
using OpenTransmute.Filtering;

namespace OpenTransmute.Plugins;

/// <summary>
/// Plugin that exposes safe file-system access to the LLM agent via Microsoft.Extensions.AI tool calling.
/// All paths are validated to stay within the configured root — any attempt to escape
/// via ".." or absolute paths throws <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class FileSystemPlugin
{
    #region Members

    private readonly string _root;
    private readonly TransmuteIgnore _ignore;

    #endregion

    #region Constructor

    /// <summary>
    /// Initialises the plugin, resolving <paramref name="rootDirectory"/> to a canonical absolute path
    /// and parsing <paramref name="ignoreContent"/> as a <c>.transmuteignore</c> rule set.
    /// </summary>
    /// <param name="rootDirectory">Absolute or relative path to the root the LLM agent may access.</param>
    /// <param name="ignoreContent">Raw contents of a <c>.transmuteignore</c> file (may be empty).</param>
    public FileSystemPlugin(string rootDirectory, string ignoreContent)
    {
        _root   = Path.GetFullPath(rootDirectory);
        _ignore = TransmuteIgnore.Load(ignoreContent);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Lists all files under the specified relative directory, recursively.
    /// Returns one entry per line in the format: path|bytes
    /// </summary>
    [Description("Lists all files under the specified relative directory, recursively. Returns one entry per line formatted as 'path|bytes'.")]
    public IEnumerable<string> ListFiles(
        [Description("The relative directory to list. Use '.' for the root.")] string relativeDirectory = ".",
        [Description("An array of file extensions to filter on. Prefix extensions with '.'. Leave null for no filter.")] List<string>? extensionFilter = null)
    {
        if (extensionFilter != null)
            return GetFileList(relativeDirectory)
                .Where(x => extensionFilter.Contains(x.fileInfo.Extension))
                .Select(f => $"{f.relPath}|{f.fileInfo.Length}");
        else
            return GetFileList(relativeDirectory).Select(f => $"{f.relPath}|{f.fileInfo.Length}");
    }

    /// <summary>
    /// Lists all file extensions under the specified relative directory, recursively.
    /// Returns one entry per line in the format: extension|count
    /// </summary>
    [Description(
        "Lists all file extensions under the specified relative directory, recursively. " +
        "Use this whenever you need to discover what kinds of files (e.g., code files) exist. " +
        "Returns one entry per line formatted as 'extension|count'. " +
        "Call this BEFORE deciding which extensions to treat as code files."
    )]
    public IEnumerable<string> ListExtensions(
        [Description("The relative directory to list. Use '.' for the root.")] string relativeDirectory = ".",
        [Description("An array of file extensions to filter on. Prefix extensions with '.'. Leave null for no filter.")] List<string>? extensionFilter = null)
    {
        if (extensionFilter != null)
            return GetFileList(relativeDirectory)
                .Where(x => extensionFilter.Contains(x.fileInfo.Extension))
                .GroupBy(x => x.fileInfo.Extension)
                .Select(x => $"{x.Key}|{x.Count()}");
        else
            return GetFileList(relativeDirectory)
                .GroupBy(x => x.fileInfo.Extension)
                .Select(x => $"{x.Key}|{x.Count()}");
    }

    /// <summary>
    /// Reads a file at the specified relative path, optionally starting at a byte offset
    /// and reading up to <paramref name="length"/> bytes.
    /// Returns empty string if the file does not exist.
    /// </summary>
    [Description("Reads a file at the specified relative path, optionally starting at a byte offset and reading up to length bytes. Returns empty string if the file does not exist.")]
    public string ReadFile(
        [Description("The relative path to the file.")] string relativePath,
        [Description("Byte offset to start reading from. 0 means start of file.")] int offset = 0,
        [Description("Number of bytes to read. 0 means read to end of file.")] int length = 0)
    {
        string full = ResolvePath(relativePath);
        if (!File.Exists(full)) return string.Empty;

        using FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (offset > 0 && offset < fs.Length)
            fs.Seek(offset, SeekOrigin.Begin);

        if (length <= 0 || offset + length > fs.Length)
            length = (int)(fs.Length - fs.Position);

        byte[] buffer = new byte[length];
        int read = fs.Read(buffer, 0, length);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>
    /// Writes or appends <paramref name="content"/> to the file at the specified relative path.
    /// Parent directories are created if they do not exist.
    /// Returns "ok" on success.
    /// </summary>
    [Description("Writes or appends content to the file at the specified relative path. Parent directories are created if they do not exist. Returns 'ok' on success.")]
    public string WriteFile(
        [Description("The relative path to the file.")] string relativePath,
        [Description("The content to write to the file.")] string content,
        [Description("If true, appends to the existing file instead of overwriting.")] bool append = false)
    {
        string full = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        using FileStream fs = new FileStream(
            full,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        fs.Write(bytes, 0, bytes.Length);
        return "ok";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private IEnumerable<(string dirName, string relPath, FileInfo fileInfo)> GetFileList(string relativeDirectory)
    {
        string dir = ResolvePath(relativeDirectory);
        if (!Directory.Exists(dir)) return Enumerable.Empty<(string dirName, string relPath, FileInfo fileInfo)>();
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                        .Select(p => (
                            dirName:  Path.GetRelativePath(_root, p).Replace('\\', '/'),
                            relPath:  Path.GetRelativePath(_root, p).Replace('\\', '/'),
                            fileInfo: new FileInfo(p)))
                        .Where(f =>
                            !_ignore.IsIgnored(f.relPath, isDirectory: false) &&
                            !_ignore.IsIgnored(f.dirName, isDirectory: true));
    }

    private string ResolvePath(string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(_root, relativePath));

        // StartsWith alone is not sufficient — "C:\Projects\foo" is a prefix of "C:\Projects\foobar",
        // which would allow traversal into a sibling directory. Require the root to be followed by
        // a separator, or be an exact match, so the check is unambiguous.
        string rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!full.Equals(_root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path escapes root: {relativePath}");

        return full;
    }

    #endregion
}
