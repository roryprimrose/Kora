using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class OllamaModelProviderFactory(
    WindowsOllamaRuntimeResolver resolver, TimeProvider time, ILoggerFactory logging,
    Func<WindowsOllamaToolEndpointBinding, IModelToolTransport>? createTransport = null) : IModelProviderRegistrationSource
{
    public async Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        CancellationToken cancellationToken)
    {
        var receipt = await resolver.ResolveAsync(selection, profile, cancellationToken).ConfigureAwait(false);
        return receipt is null ? null : new(selection,
            ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
            receipt.Catalogue.ValidUntil, new OllamaModelTurnAdapter(resolver, receipt, time, logging, createTransport),
            () => receipt.IsCurrent(time.GetUtcNow()));
    }
}
