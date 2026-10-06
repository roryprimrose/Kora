using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

internal sealed class CancelPublicationCheckpoint(CancellationTokenSource source) : IStoragePublicationCheckpoint
{
    public ValueTask AfterFlushAsync(StoragePublicationKind kind, CancellationToken cancellationToken)
    {
        source.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
