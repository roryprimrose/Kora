namespace Kora.Windows.Dependencies;

public sealed class UnavailableCopilotDeploymentApprovalSource : ICopilotDeploymentApprovalSource
{
    public ValueTask<CopilotDeploymentApproval?> ResolveAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult<CopilotDeploymentApproval?>(null);
    }
}
