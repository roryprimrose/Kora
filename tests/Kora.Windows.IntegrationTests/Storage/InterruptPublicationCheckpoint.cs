using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

internal sealed class InterruptPublicationCheckpoint(StoragePublicationKind kind) : IStoragePublicationCheckpoint
{
    public ValueTask AfterFlushAsync(StoragePublicationKind publicationKind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return publicationKind == kind
            ? ValueTask.FromException(new IOException("Simulated interruption after staging flush."))
            : ValueTask.CompletedTask;
    }
}
