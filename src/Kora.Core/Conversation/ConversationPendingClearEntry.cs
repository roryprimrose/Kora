using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>An exact never-dispatched original and its observed FIFO position and unchanged expiry.</summary>
public sealed record ConversationPendingClearEntry(HostRequest Request, HostRevision TaskRevision,
    long MessageRevision, long Sequence, int Position, DateTimeOffset ExpiresAt);
