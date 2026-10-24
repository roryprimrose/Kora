namespace Kora.Windows.Dependencies;

/// <summary>Host approval of exact runtime deployment, private paths and account-use terms/cost policy.</summary>
public interface ICopilotDeploymentApprovalSource
{
    /// <summary>Revalidates the exact approved deployment. Unknown provenance, ownership or policy returns null.</summary>
    ValueTask<CopilotDeploymentApproval?> ResolveAsync(CancellationToken token);
}
