using System.Collections.Immutable;

namespace Kora.Core.Conversation.SessionRouting;

public sealed record SessionRouteProposal(
    long ExpectedRevision, SessionRouteMatch Match, ImmutableArray<SessionRouteCandidate> Candidates);
