using OpenTransmute.Models;

namespace OpenTransmute.Source;

public record SourceResult(
    string LocalPath,
    string InferredProjectName,
    bool IsTemporary
);

public interface ISourceFetcher
{
    bool CanHandle(string source);
    Task<SourceResult> FetchAsync(string source, DecomposeOptions options, CancellationToken ct = default);
}
