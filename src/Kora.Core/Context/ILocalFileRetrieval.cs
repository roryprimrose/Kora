namespace Kora.Core.Context;

public interface ILocalFileRetrieval
{
    /// <summary>Searches the exact admitted single-file snapshot using the original policy.</summary>
    LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource,
        string query, DateTimeOffset observedAt, CancellationToken cancellationToken);

    /// <summary>Searches only the complete exact admitted folder revision with a shared result budget.</summary>
    LocalFileSearchResult Search(LocalFolderRevision revision, LocalFolderReference exactSource,
        string query, DateTimeOffset observedAt, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Folder-scoped lexical retrieval is unavailable.");

    /// <summary>Searches an exact admitted file with an explicitly selected run-only mode.</summary>
    LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource, string query,
        LocalFileRetrievalMode mode, DateTimeOffset observedAt, CancellationToken cancellationToken) =>
        mode == LocalFileRetrievalMode.LexicalLines ? Search(revision, exactSource, query, observedAt, cancellationToken)
            : LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, observedAt, mode);

    /// <summary>Searches the exact complete folder with one global budget and a run-only mode.</summary>
    LocalFileSearchResult Search(LocalFolderRevision revision, LocalFolderReference exactSource, string query,
        LocalFileRetrievalMode mode, DateTimeOffset observedAt, CancellationToken cancellationToken) =>
        mode == LocalFileRetrievalMode.LexicalLines ? Search(revision, exactSource, query, observedAt, cancellationToken)
            : LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, observedAt, mode);
}
