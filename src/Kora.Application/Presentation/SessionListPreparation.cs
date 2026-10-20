using Kora.Core.Hosting;

namespace Kora.Application.Presentation;

/// <summary>A scoped original-input handoff. The ID is query data only.</summary>
public sealed class SessionListPreparation
{
    internal SessionListPreparation(HostId<SessionIdentity> sessionId, Func<bool> admitted)
    {
        SessionId = sessionId;
        IsAdmitted = admitted;
    }

    public HostId<SessionIdentity> SessionId { get; }
    public Func<bool> IsAdmitted { get; }
}
