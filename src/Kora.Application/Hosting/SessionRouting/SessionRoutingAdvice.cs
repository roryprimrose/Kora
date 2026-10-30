using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting.SessionRouting;

/// <summary>Exact volatile input and permitted metadata disclosed before one local relatedness inference.</summary>
public sealed record SessionRoutingAdvice(
    string Body, RequestOrigin Origin, SessionRouteSnapshot Snapshot, DateTimeOffset ExpiresAt);
