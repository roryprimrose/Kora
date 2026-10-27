using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

public sealed record ConversationPendingSnapshot(HostId<SessionIdentity> Session, long Revision,
    bool Paused, ImmutableArray<ConversationPendingTurn> Turns)
{
    public long ControlRevision { get; init; }
}
