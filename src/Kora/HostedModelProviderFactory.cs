using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora;

internal sealed class HostedModelProviderFactory(CopilotModelProviderFactory existing, ClaudeModelProviderFactory claude)
    : IModelProviderRegistrationSource
{
    public Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        CancellationToken cancellationToken) => selection.Provider == ModelProviderIdentity.Claude
        ? claude.ResolveAsync(selection, profile, cancellationToken)
        : existing.ResolveAsync(selection, profile, cancellationToken);
}
