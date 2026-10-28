namespace Kora.Core.Conversation.SessionRouting;

/// <summary>Local-only inference over the current request and bounded permitted metadata, without history or tools.</summary>
public interface ISessionRelatednessMatcher
{
    Task<SessionRouteProposal> MatchAsync(string request, SessionRouteSnapshot snapshot,
        Func<bool> admitted, CancellationToken token);
}
