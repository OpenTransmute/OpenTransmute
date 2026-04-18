using LibGit2Sharp;
using OpenTransmute.Models;

namespace OpenTransmute.Source;

/// <summary>
/// Handles remote git sources (HTTP/HTTPS URLs and git@ SSH URLs).
/// Clones the repository to a temporary directory and returns it as a temporary source result.
/// </summary>
public class GitSourceFetcher(ILogger<GitSourceFetcher> logger) : ISourceFetcher
{
    #region Methods

    /// <summary>
    /// Returns true when the source string is an HTTP(S) or git@ URL.
    /// </summary>
    public bool CanHandle(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("git@", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Clones the repository to a unique temporary directory and returns its path.
    /// The result is marked temporary so <c>JobRunner</c> can clean it up after the run.
    /// </summary>
    /// <param name="source">A git URL (HTTP/HTTPS or SSH).</param>
    /// <param name="options">Decompose options; <c>ProjectName</c> overrides the inferred repo name if set.</param>
    /// <param name="ct">Not used — LibGit2Sharp clone is synchronous.</param>
    /// <returns>A temporary <see cref="SourceResult"/> pointing at the cloned directory.</returns>
    public Task<SourceResult> FetchAsync(string source, DecomposeOptions options, CancellationToken ct = default)
    {
        string cloneRoot = options.CloneDirectory ?? Path.GetTempPath();
        string repoName = InferRepoName(source);
        string destPath = Path.Combine(cloneRoot, $"opentransmute-{repoName}-{Guid.NewGuid():N}");

        logger.LogInformation("Cloning {Source} to {Dest}", source, destPath);

        CloneOptions cloneOptions = new CloneOptions
        {
            RecurseSubmodules = false
        };

        Repository.Clone(source, destPath, cloneOptions);

        string projectName = string.IsNullOrWhiteSpace(options.ProjectName) ? repoName : options.ProjectName;
        return Task.FromResult(new SourceResult(destPath, projectName, IsTemporary: true));
    }

    private static string InferRepoName(string url)
    {
        string last = url.TrimEnd('/').Split('/').LastOrDefault() ?? "repo";
        return last.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? last[..^4]
            : last;
    }

    #endregion
}
