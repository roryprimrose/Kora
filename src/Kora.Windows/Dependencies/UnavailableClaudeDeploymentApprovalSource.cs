namespace Kora.Windows.Dependencies;

public sealed class UnavailableClaudeDeploymentApprovalSource : IClaudeDeploymentApprovalSource
{
    public ValueTask<ClaudeDeploymentApproval?> ResolveAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<ClaudeDeploymentApproval?>(null); }
    public ValueTask<ClaudeCredentialLease?> BindCredentialAsync(ClaudeDeploymentApproval approval,
        string credentialSha256, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<ClaudeCredentialLease?>(null); }
}
