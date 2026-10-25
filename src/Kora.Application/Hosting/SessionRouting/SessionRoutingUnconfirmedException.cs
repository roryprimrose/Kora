using Kora.Core.Hosting;

namespace Kora.Application.Hosting.SessionRouting;

public sealed class SessionRoutingUnconfirmedException(HostRequest creation, Exception cause)
    : IOException("Routing creation or subsequent submission is unconfirmed. Inspect the exact creation and any submission; no rollback or retry.", cause)
{
    public HostRequest Creation { get; } = creation;
}
