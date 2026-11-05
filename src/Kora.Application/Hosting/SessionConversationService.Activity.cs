using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public SubmittedWorkActivity ReadSubmittedWorkActivity()
    {
        var control = access.ControlRevision;
        var observed = time.GetUtcNow();
        lock (cancellationGate)
        {
            if (!access.CanInspect || pendingClosed)
            { return new(observed, false, false, 0, 0, 0, 0, 0, 0, 0, false); }

            var busy = 0;
            var waiting = 0;
            var cancelling = 0;
            var retiring = 0;
            foreach (var lease in runningConversations.Values)
            {
                var phase = lease.Status.Phase;
                if (phase == ConversationExecutionPhase.Cancelling) { cancelling++; }
                else if (phase is ConversationExecutionPhase.Completing or ConversationExecutionPhase.Terminal
                    || lease.Token.IsCancellationRequested || !lease.CanContinue)
                { retiring++; }
                else if (phase == ConversationExecutionPhase.Waiting) { waiting++; }
                else { busy++; }
            }

            var entries = conversationLanes.Values.SelectMany(lane => lane.Entries).ToArray();
            var result = new SubmittedWorkActivity(observed, true, lifecycleUnconfirmed, busy, waiting, cancelling, retiring,
                entries.Count(entry => entry.Retained is not null && entry.Phase == ConversationPendingPhase.Pending),
                entries.Count(entry => entry.Retained is null),
                conversationLanes.Values.Count(lane => lane.Paused || lane.PendingClearHeld
                    || lane.ManualHoldReason != ConversationDispatchHoldReason.None), lifecycleHeld);
            return access.CanInspect && access.ControlRevision == control
                ? result : new(observed, false, false, 0, 0, 0, 0, 0, 0, 0, false);
        }
    }
}
