using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Single-use host-owned review. Retained metadata is evidence, never renewed source authority.</summary>
public sealed class ConversationPendingClearReview
{
    internal ConversationPendingClearReview(SessionWorkspaceEntry target, ConversationPendingClearSnapshot snapshot,
        long controlRevision, Func<bool> sourceCurrent)
    { Target = target; Snapshot = snapshot; ControlRevision = controlRevision; SourceCurrent = sourceCurrent; }

    public SessionWorkspaceEntry Target { get; }
    public ConversationPendingClearSnapshot Snapshot { get; }
    internal long ControlRevision { get; }
    internal Func<bool> SourceCurrent { get; }
}
