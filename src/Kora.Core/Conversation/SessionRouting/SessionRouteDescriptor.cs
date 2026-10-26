using Kora.Core.Storage;

namespace Kora.Core.Conversation.SessionRouting;

public sealed record SessionRouteDescriptor(SessionWorkspaceEntry Entry, Guid EvidenceId)
{
    public string Name => Entry.Metadata?.Name.Value ?? "Unnamed conversation";
    public SessionRouteCandidate Candidate => new(Entry.Authority.SessionId, EvidenceId);
}
