using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed record RemainingWorkTurn(HostRequest Request, HostRevision Generation, HostRevision TaskRevision,
    DateTimeOffset ExpiresAt, ConversationPendingPhase Phase, int Position);