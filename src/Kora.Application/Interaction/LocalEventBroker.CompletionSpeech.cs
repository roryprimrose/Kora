using Kora.Core.Interaction;
using Kora.Core.Hosting;

namespace Kora.Application.Interaction;

public sealed partial class LocalEventBroker
{
    internal TimeProvider CompletionSpeechClock => time;

    internal async Task WithCompletionConsentSourcesAsync(HostId<SessionIdentity> session,
        Action<IReadOnlyList<LocalEvent>> commit, Func<bool> eligible, CancellationToken token)
    {
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Require(eligible);
            var observed = await source.ReadAsync(session, token).ConfigureAwait(false);
            await source.WithCurrentAsync(session, observed, () =>
            {
                token.ThrowIfCancellationRequested();
                Require(eligible);
                commit(observed);
                return true;
            }, token).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }

    internal async Task StartCompletionSpeechAsync(LocalEventSnapshot snapshot, LocalEvent target,
        Func<bool> hostEligible, Action start, CancellationToken token)
    {
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            bool Eligible() => IsCurrent(snapshot) && hostEligible() && !routineQuiet.Enabled
                && target.Source == LocalEventSource.ConversationTask && target.Type == LocalEventType.Completed
                && snapshot.Events.Any(view => view.Event == target && view.Reason == LocalEventReason.Eligible)
                && state is not null && time.GetUtcNow() >= state.HighWatermark && time.GetUtcNow() < target.ExpiresAt
                && state.Receipts.Any(receipt => receipt.Event == target
                    && receipt.Disposition == LocalEventDisposition.Presented);
            Require(Eligible);
            var observed = await source.ReadAsync(target.SessionId, token).ConfigureAwait(false);
            await source.WithCurrentAsync(target.SessionId, observed, () =>
            {
                token.ThrowIfCancellationRequested();
                Require(Eligible);
                if (!observed.Any(target.SameSource) || observed.Any(item => item.Type == LocalEventType.UserAttention))
                { throw new InvalidOperationException("The exact completion source is no longer current."); }
                start();
                return true;
            }, token).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
}