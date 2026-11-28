using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

internal sealed class ClaudeAccount : IDisposable
{
    private readonly CancellationTokenSource revocation = new();
    internal ClaudeAccount(string credential, ClaudeDeploymentApproval approval, ClaudeCredentialLease lease,
        ProviderCapabilityCatalogue catalogue, IReadOnlyDictionary<ProviderChoiceId, int> outputLimits)
    {
        Credential = credential; Approval = approval; Lease = lease; this.catalogue = catalogue; OutputLimits = outputLimits;
        Revocation = revocation.Token;
    }
    internal string Credential { get; }
    internal ClaudeDeploymentApproval Approval { get; }
    internal ClaudeCredentialLease Lease { get; }
    private ProviderCapabilityCatalogue catalogue;
    /// <summary>The account's current catalogue snapshot; only the owning broker renews its validity in place.</summary>
    internal ProviderCapabilityCatalogue Catalogue => Volatile.Read(ref catalogue);
    internal void Renew(DateTimeOffset validUntil)
    {
        var current = Catalogue;
        Volatile.Write(ref catalogue, new(current.Service, current.Status, current.Account, current.Revision,
            current.ObservedAt, validUntil, current.Models));
    }
    internal IReadOnlyDictionary<ProviderChoiceId, int> OutputLimits { get; }
    internal CancellationToken Revocation { get; }
    public void Dispose()
    {
        try { revocation.Cancel(); }
        finally { revocation.Dispose(); }
    }
}
