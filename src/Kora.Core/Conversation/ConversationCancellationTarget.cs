using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Exact host-issued running conversation identity; display text cannot select this target.</summary>
public sealed record ConversationCancellationTarget(Guid LeaseId, HostRequest Request, HostRevision Generation,
    HostRevision TaskRevision, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
