using Kora.Core.Providers;

namespace Kora.Application.Configuration;

public sealed class SessionProviderProposal
{
    internal SessionProviderProposal(SessionProviderDraft draft, SessionProviderOverrides desired, SessionProviderField? field,
        ProviderProfileTuple? effective, DateTimeOffset expiresAt, SessionProviderDraft? source = null)
    {
        Draft = draft;
        Desired = desired;
        Field = field;
        ResultingEffective = effective;
        ExpiresAt = expiresAt;
        Source = source;
    }
    public SessionProviderDraft Draft { get; }
    public SessionProviderOverrides Desired { get; }
    public SessionProviderField? Field { get; }
    public ProviderProfileTuple? PreviousEffective => Draft.Effective;
    public ProviderProfileTuple? ResultingEffective { get; }
    public long AppRevision => Draft.AppRevision;
    public SessionProviderDraft? Source { get; }
    public DateTimeOffset ExpiresAt { get; }
    public Guid ConfirmationId { get; } = Guid.NewGuid();
}
