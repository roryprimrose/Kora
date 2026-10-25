using Kora.Core.Providers;
using Kora.Windows.Dependencies;

namespace Kora;

internal sealed class HostedProviderCatalogue(CopilotProviderCatalogue existing, ClaudeAccountBroker claude)
    : IProviderCapabilityCatalogue
{
    public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        var baseline = await existing.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await claude.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return new(baseline.Installation, baseline.Catalogues.Select(item =>
            item.Service == ProviderService.Claude ? snapshot : item).ToArray());
    }
}
