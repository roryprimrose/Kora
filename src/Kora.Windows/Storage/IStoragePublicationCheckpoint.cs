namespace Kora.Windows.Storage;

internal interface IStoragePublicationCheckpoint
{
    ValueTask AfterFlushAsync(StoragePublicationKind kind, CancellationToken cancellationToken);
}
