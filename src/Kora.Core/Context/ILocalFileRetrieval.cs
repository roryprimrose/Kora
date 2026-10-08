namespace Kora.Core.Context;

public interface ILocalFileRetrieval
{
    LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource,
        string query, DateTimeOffset observedAt, CancellationToken cancellationToken);
}
