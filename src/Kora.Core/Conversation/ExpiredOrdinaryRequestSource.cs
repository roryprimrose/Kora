using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Exact host-confirmed pre-dispatch expiry, never a dispatch or resource-join authority.</summary>
public sealed record ExpiredOrdinaryRequestSource(ConversationMessage Message, HostTaskRecord Task)
{
    public void Validate()
    {
        Message.Validate();
        if (!Message.OrdinaryText || Message.Version != 1 || Message.Handoff is not null
            || Message.Outcome != ModelTurnOutcome.Denied || Message.Reason != ModelTurnReason.ContextExpired
            || Task.Request != Message.Request || Task.Revision.Value != 2 || Task.State != HostTaskState.Denied)
        { throw new InvalidOperationException("Fresh review requires host-recorded bare ordinary text and confirmed pre-dispatch expiry. Legacy, specialized, interrupted, dispatched or uncertain sources are unavailable."); }
    }
}
