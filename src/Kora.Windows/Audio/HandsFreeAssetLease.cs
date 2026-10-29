namespace Kora.Windows.Audio;

internal sealed class HandsFreeAssetLease(IReadOnlyList<FileStream> streams, IAsyncDisposable? ownership = null) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        foreach (var stream in streams) { await stream.DisposeAsync().ConfigureAwait(false); }
        if (ownership is not null) { await ownership.DisposeAsync().ConfigureAwait(false); }
    }
}
