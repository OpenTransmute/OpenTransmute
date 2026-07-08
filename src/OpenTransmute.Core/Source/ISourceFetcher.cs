using OpenTransmute.Models;

namespace OpenTransmute.Source;

/// <summary>
/// Result of resolving a decompose source to a local working directory.
/// </summary>
/// <param name="LocalPath">Absolute path to the source on disk that the orchestrator will read.</param>
/// <param name="InferredProjectName">Project name derived from the source (e.g. a git repo name) when none was supplied.</param>
/// <param name="IsTemporary">True when <see cref="LocalPath"/> is a temp clone/checkout the caller should clean up after use.</param>
public record SourceResult(
    string LocalPath,
    string InferredProjectName,
    bool IsTemporary
);

/// <summary>
/// Resolves a decompose source string (local path, git URL, etc.) into a local directory the
/// orchestrator can scan. Implementations are tried in order via <see cref="CanHandle"/>.
/// </summary>
public interface ISourceFetcher
{
    /// <summary>Returns true when this fetcher recognizes and can resolve the given source string.</summary>
    bool CanHandle(string source);

    /// <summary>
    /// Materializes the source locally and returns its on-disk location plus inferred metadata.
    /// </summary>
    Task<SourceResult> FetchAsync(string source, DecomposeOptions options, CancellationToken ct = default);
}
