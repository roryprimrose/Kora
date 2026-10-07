namespace Kora.Windows.Audio;

public sealed partial class WindowsTextToSpeechService
{
    internal async Task RunCurrentOutputAsync(Func<long, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var generation = Interlocked.Read(ref outputGeneration);
        await speechLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireCurrentOutput(generation);
            await operation(generation, cancellationToken).ConfigureAwait(false);
            RequireCurrentOutput(generation);
        }
        finally { speechLock.Release(); }
    }

    private void RequireCurrentOutput(long generation)
    {
        if (disposed || generation != Interlocked.Read(ref outputGeneration))
        {
            throw new OperationCanceledException("Speech output was invalidated; no completed playback is claimed.");
        }
    }
}
