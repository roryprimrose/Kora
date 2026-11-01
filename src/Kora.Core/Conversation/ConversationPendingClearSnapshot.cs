using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Complete body-free identities of one ordinary pending lane, not diagnostic queue authority.</summary>
public sealed record ConversationPendingClearSnapshot(Guid Id, HostId<SessionIdentity> Session,
    HostRevision Generation, long MetadataRevision, long LaneRevision, RequestOrigin Origin,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    ImmutableArray<ConversationPendingClearEntry> Entries, ConversationExecutionStatus? Running)
{
    public int Count => Entries.Length;
}
