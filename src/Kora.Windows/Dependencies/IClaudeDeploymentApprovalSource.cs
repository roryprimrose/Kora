namespace Kora.Windows.Dependencies;

/// <summary>Independent host approval of the native HTTPS profile, account-use terms, cost and exact credential lease.</summary>
public interface IClaudeDeploymentApprovalSource
{
    ValueTask<ClaudeDeploymentApproval?> ResolveAsync(CancellationToken token);
    ValueTask<ClaudeCredentialLease?> BindCredentialAsync(ClaudeDeploymentApproval approval,
        string credentialSha256, CancellationToken token);
}
