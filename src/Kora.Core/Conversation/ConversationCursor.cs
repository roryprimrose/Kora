using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

public sealed record ConversationCursor(HostId<SessionIdentity> SessionId, HostRevision Generation,
    long Ceiling, long After, long HeadRevision);
