using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

internal sealed partial class ListeningStopAction(IActivatedVoiceRecognitionService capture, ILogger logger)
{
    private int held;
    private int stopping;

    public bool IsHeld => Volatile.Read(ref held) != 0;

    public bool IsStopping => Volatile.Read(ref stopping) != 0;

    public bool CanRecover => !IsStopping && capture.IsCaptureQuiescent;

    public bool TryRecover()
    {
        if (!CanRecover) { return false; }
        Interlocked.Exchange(ref held, 0);
        return true;
    }

    public async Task<ListeningStopOutcome> DisableAsync(Action retireInput)
    {
        var alreadyDisabled = Interlocked.Exchange(ref held, 1) != 0;
        Interlocked.Increment(ref stopping);
        try
        {
            // Close host generations before the first await, including an in-flight native open.
            try { retireInput(); }
            finally { await capture.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            var outcome = capture.IsCaptureQuiescent
                ? alreadyDisabled ? ListeningStopOutcome.AlreadyDisabled : ListeningStopOutcome.Disabled
                : ListeningStopOutcome.ReleaseUnconfirmed;
            Stopped(logger, outcome);
            return outcome;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReleaseFailed(logger, exception);
            return ListeningStopOutcome.ReleaseUnconfirmed;
        }
        finally
        {
            Interlocked.Decrement(ref stopping);
        }
    }
}
