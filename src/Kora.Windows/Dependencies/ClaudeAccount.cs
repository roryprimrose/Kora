using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

internal sealed class ClaudeAccount : IDisposable
{
    private readonly CancellationTokenSource revocation = new();
    internal ClaudeAccount(string credential, ClaudeDeploymentApproval approval, ClaudeCredentialLease lease,
        ProviderCapabilityCatalogue catalogue, IReadOnlyDictionary<ProviderChoiceId, int> outputLimits)
    {
        Credential = credential; Approval = approval; Lease = lease; Catalogue = catalogue; OutputLimits = outputLimits;
        Revocation = revocation.Token;
    }
    internal string Credential { get; }
    internal ClaudeDeploymentApproval Approval { get; }
    internal ClaudeCredentialLease Lease { get; }
    internal ProviderCapabilityCatalogue Catalogue { get; }
    internal IReadOnlyDictionary<ProviderChoiceId, int> OutputLimits { get; }
    internal CancellationToken Revocation { get; }
    public void Dispose()
    {
        try { revocation.Cancel(); }
        finally { revocation.Dispose(); }
    }
}
