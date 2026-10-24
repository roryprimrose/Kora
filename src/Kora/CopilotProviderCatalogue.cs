using Kora.Core.Providers;
using Kora.Windows.Dependencies;

namespace Kora;

internal sealed class CopilotProviderCatalogue(WindowsOllamaRuntimeResolver local, CopilotAccountBroker copilot)
    : IProviderCapabilityCatalogue
{
    public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        var baseline = await local.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var hosted = await copilot.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return new(baseline.Installation, baseline.Catalogues.Select(item =>
            item.Service == ProviderService.Copilot ? hosted : item).ToArray());
    }
}
