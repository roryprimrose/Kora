using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Resolves explicit host-approved credentials and runtime bytes, never ambient Copilot or gh login.</summary>
public interface ICopilotExecutionAccountSource
{
    ValueTask<CopilotExecutionAccount?> ResolveAsync(ProviderProfileTuple profile, CancellationToken cancellationToken);
}
