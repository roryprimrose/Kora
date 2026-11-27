using System.Collections.Immutable;

using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting.SessionRouting;

public sealed class SessionRoutingReview
{
    /// <summary>The host-owned reason for proposing a destination, never submission authority.</summary>
    public enum PreselectionBasis
    {
        /// <summary>The exact foreground conversation at original voice admission.</summary>
        Foreground,
    }

    public sealed record PreselectedDestination(SessionRouteDescriptor Destination, PreselectionBasis Basis);

    internal SessionRoutingReview(string body, RequestOrigin origin, SessionRouteSnapshot snapshot,
        SessionRouteMatch match, ImmutableArray<SessionRouteDescriptor> candidates, DateTimeOffset expiresAt,
        Func<HostActivity> continuation, PreselectedDestination? preselection = null)
    {
        Body = body;
        Origin = origin;
        Snapshot = snapshot;
        Match = match;
        Candidates = candidates;
        ExpiresAt = expiresAt;
        Continue = continuation;
        Preselection = preselection;
    }

    public string Body { get; }
    public RequestOrigin Origin { get; }
    public SessionRouteSnapshot Snapshot { get; }
    public SessionRouteMatch Match { get; }
    public ImmutableArray<SessionRouteDescriptor> Candidates { get; }
    public DateTimeOffset ExpiresAt { get; }
    public PreselectedDestination? Preselection { get; }
    internal Func<HostActivity> Continue { get; }
}
