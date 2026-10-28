namespace Kora.Windows.Audio;

internal sealed class HandsFreeAssetLease(IReadOnlyList<FileStream> streams) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        foreach (var stream in streams) { await stream.DisposeAsync().ConfigureAwait(false); }
    }
}
