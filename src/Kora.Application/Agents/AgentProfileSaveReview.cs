using Kora.Core.Agents;
using Kora.Core.Hosting;

namespace Kora.Application.Agents;

public sealed class AgentProfileSaveReview : IDisposable
{
    private int consumed;
    internal AgentProfileSaveReview(AgentProfileSaveWorkflow owner, OwnedAgentProfileDestination destination,
        OwnedAgentProfileDocument document, long revision, RequestOrigin origin, Func<bool> eligible)
    { Owner = owner; Destination = destination; Document = document; Revision = revision; Origin = origin; Eligible = eligible; }

    internal AgentProfileSaveWorkflow Owner { get; }
    internal Func<bool> Eligible { get; }
    internal RequestOrigin Origin { get; }
    public OwnedAgentProfileDestination Destination { get; }
    public OwnedAgentProfileDocument Document { get; }
    public long Revision { get; }
    public bool IsCurrent => Volatile.Read(ref consumed) == 0 && Eligible();
    internal void Consume()
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0 || !Eligible())
        { throw new InvalidOperationException("The exact new-file review expired or was already consumed."); }
    }
    public void Dispose() => Interlocked.Exchange(ref consumed, 1);
}
