using Kora.Core.Providers;

namespace Kora.Application.Configuration;

public sealed class SessionProviderProposal
{
    internal SessionProviderProposal(SessionProviderDraft draft, SessionProviderOverrides desired) { Draft = draft; Desired = desired; }
    public SessionProviderDraft Draft { get; }
    public SessionProviderOverrides Desired { get; }
    public Guid ConfirmationId { get; } = Guid.NewGuid();
}
