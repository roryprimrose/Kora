using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Communication;

internal sealed class CallSpeechLease(CallSpeechReview review, HostActivity confirmation) : IDisposable
{
    private int started;
    private int releaseUnconfirmed;

    internal string Text => review.Text;
    internal AudioOutputDevice Output => review.Output;
    internal ResponseSpeechTarget Target => review.Target;
    internal CancellationToken Expiration => review.Expiration;
    internal bool AllowsContinuation => review.Current() && !confirmation.Activity!.IsStopped
        && confirmation.Outcome == HostOperationOutcome.Unknown;
    internal bool IsPlaybackCurrent => Volatile.Read(ref started) == 1 && AllowsContinuation;
    internal bool ReleaseUnconfirmed => Volatile.Read(ref releaseUnconfirmed) != 0;
    internal void HoldUnconfirmedRelease() => Interlocked.Exchange(ref releaseUnconfirmed, 1);
    internal bool IsCurrent => AllowsContinuation && HostActivity.Current is { } activity
        && activity.Request == confirmation.Request && !activity.Activity!.IsStopped
        && activity.Outcome == HostOperationOutcome.Unknown;

    internal bool TryStart(CallCommunicationPolicy policy, string text) =>
        Interlocked.Exchange(ref started, 1) == 0 && review.BelongsTo(policy)
        && string.Equals(Text, text, StringComparison.Ordinal) && IsCurrent;

    public void Dispose() => review.Dispose();
}
