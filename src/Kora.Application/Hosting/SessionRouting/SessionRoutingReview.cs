using System.Collections.Immutable;

using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting.SessionRouting;

public sealed class SessionRoutingReview
{
    internal SessionRoutingReview(string body, RequestOrigin origin, SessionRouteSnapshot snapshot,
        SessionRouteMatch match, ImmutableArray<SessionRouteDescriptor> candidates, DateTimeOffset expiresAt,
        Func<HostActivity> continuation)
    {
        Body = body;
        Origin = origin;
        Snapshot = snapshot;
        Match = match;
        Candidates = candidates;
        ExpiresAt = expiresAt;
        Continue = continuation;
    }

    public string Body { get; }
    public RequestOrigin Origin { get; }
    public SessionRouteSnapshot Snapshot { get; }
    public SessionRouteMatch Match { get; }
    public ImmutableArray<SessionRouteDescriptor> Candidates { get; }
    public DateTimeOffset ExpiresAt { get; }
    internal Func<HostActivity> Continue { get; }
}
