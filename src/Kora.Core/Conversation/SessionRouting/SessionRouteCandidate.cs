using Kora.Core.Hosting;

namespace Kora.Core.Conversation.SessionRouting;

/// <summary>An opaque reference to one host-reviewed metadata descriptor, never execution authority.</summary>
public sealed record SessionRouteCandidate(HostId<SessionIdentity> SessionId, Guid EvidenceId);
