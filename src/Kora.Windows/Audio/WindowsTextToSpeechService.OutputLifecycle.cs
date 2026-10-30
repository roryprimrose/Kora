namespace Kora.Windows.Audio;

public sealed partial class WindowsTextToSpeechService
{
    internal async Task JoinRejectedSynthesisAsync(Task synthesis, CancellationTokenSource cancellation)
    {
        await cancellation.CancelAsync().ConfigureAwait(false);
        cancelOwnedSynthesis();
#pragma warning disable VSTHRD003 // Joins the synthesis started by this same serialized output owner before releasing its resources.
        try { await synthesis.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

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
