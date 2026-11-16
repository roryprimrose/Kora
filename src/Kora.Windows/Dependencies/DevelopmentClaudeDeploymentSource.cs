using System.Security.Cryptography;
using System.Text;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Explicit preview scope; credential entry and every physical send retain their independent reviews.</summary>
public sealed class DevelopmentClaudeDeploymentSource(DevelopmentPreviewAdmission admission) : IClaudeDeploymentApprovalSource
{
    private readonly Lock gate = new();
    private DevelopmentPreviewLease? current;
    private ClaudeDeploymentApproval? approval;
    private ClaudeCredentialLease? credential;

    public ValueTask<ClaudeDeploymentApproval?> ResolveAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            var admitted = admission.Resolve(DevelopmentPreviewCapability.Claude);
            if (!ReferenceEquals(current, admitted))
            {
                current = admitted;
                approval = admitted is null ? null : new(admitted);
                credential = null;
            }
            return ValueTask.FromResult(approval);
        }
    }

    public ValueTask<ClaudeCredentialLease?> BindCredentialAsync(ClaudeDeploymentApproval approval,
        string credentialSha256, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (current is null || !current.IsCurrent || !ReferenceEquals(this.approval, approval)
                || !LocalModelQualificationEvidence.IsHash(credentialSha256, 64))
            { throw new InvalidOperationException("The exact Claude development preview review is unavailable or expired."); }
            if (credential is null)
            {
                var id = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(credentialSha256)));
                credential = new(new ProviderAccountId("development." + id), "development_preview",
                    credentialSha256, current.ExpiresAt);
            }
            return ValueTask.FromResult(string.Equals(credential.CredentialSha256, credentialSha256, StringComparison.Ordinal)
                ? credential : null);
        }
    }
}
