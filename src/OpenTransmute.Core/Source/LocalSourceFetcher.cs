using OpenTransmute.Models;

namespace OpenTransmute.Source;

/// <summary>
/// Handles local directory sources (paths that are not git URLs).
/// Resolves the path and returns it as a non-temporary source result.
/// </summary>
public class LocalSourceFetcher : ISourceFetcher
{
    #region Methods

    /// <summary>
    /// Returns true when the source string is a local path (not an HTTP or git URL).
    /// </summary>
    public bool CanHandle(string source) =>
        !source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
        !source.StartsWith("git@", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the local path and returns it as a source result.
    /// </summary>
    /// <param name="source">A local filesystem path (absolute or relative).</param>
    /// <param name="options">Decompose options; <c>ProjectName</c> overrides the directory name if set.</param>
    /// <param name="ct">Not used — included for interface compatibility.</param>
    /// <returns>A <see cref="SourceResult"/> pointing at the resolved local directory.</returns>
    public Task<SourceResult> FetchAsync(string source, DecomposeOptions options, CancellationToken ct = default)
    {
        string path = Path.GetFullPath(source);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Source directory not found: {path}");

        string name = string.IsNullOrWhiteSpace(options.ProjectName)
            ? new DirectoryInfo(path).Name
            : options.ProjectName;

        return Task.FromResult(new SourceResult(path, name, IsTemporary: false));
    }

    #endregion
}
