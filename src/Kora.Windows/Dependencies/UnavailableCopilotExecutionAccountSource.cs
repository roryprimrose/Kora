using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>No account is inferred from preferences, process environment or the host's own sign-in.</summary>
public sealed class UnavailableCopilotExecutionAccountSource : ICopilotExecutionAccountSource
{
    public ValueTask<CopilotExecutionAccount?> ResolveAsync(ProviderProfileTuple profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<CopilotExecutionAccount?>(null);
    }
}
