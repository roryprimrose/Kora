using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Resolves explicit host-approved credentials and runtime bytes, never ambient Copilot or gh login.</summary>
public interface ICopilotExecutionAccountSource
{
    ValueTask<CopilotExecutionAccount?> ResolveAsync(ProviderProfileTuple profile, CancellationToken cancellationToken);

    /// <summary>Audited in-place lease revalidation for a running host turn; sources without leases renew nothing.</summary>
    ValueTask<bool> RenewAsync(CopilotExecutionAccount expected, CancellationToken token) => ValueTask.FromResult(false);
}
