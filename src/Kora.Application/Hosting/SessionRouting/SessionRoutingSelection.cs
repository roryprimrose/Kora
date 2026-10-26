using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.SessionRouting;

public sealed class SessionRoutingSelection
{
    internal SessionRoutingSelection(SessionRoutingReview review, SessionWorkspaceEntry? target)
    { Review = review; Target = target; }

    public SessionRoutingReview Review { get; }
    public SessionWorkspaceEntry? Target { get; internal set; }
    public HostRequest? CreationRequest { get; internal set; }
    public string Acknowledgment => Target is { } target
        ? "Destination: " + (target.Metadata?.Name.Value ?? "Unnamed conversation") + "."
        : "Destination: New conversation (a fresh session).";
}
