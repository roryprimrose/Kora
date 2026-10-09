namespace Kora.Core.Context;

public interface ILocalFileRetrieval
{
    LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource,
        string query, DateTimeOffset observedAt, CancellationToken cancellationToken);

    /// <summary>Searches only the complete exact admitted folder revision with a shared result budget.</summary>
    LocalFileSearchResult Search(LocalFolderRevision revision, LocalFolderReference exactSource,
        string query, DateTimeOffset observedAt, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Folder-scoped lexical retrieval is unavailable.");
}
