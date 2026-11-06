using Kora.Core.Providers;
using Kora.Windows.Dependencies;

namespace Kora;

internal sealed class CopilotProviderCatalogue(WindowsOllamaRuntimeResolver local, CopilotAccountBroker copilot)
    : IProviderCapabilityCatalogue, IProviderCapabilityRefresh
{
    public async Task<ProviderCatalogueObservation> RefreshAsync(ProviderService service, Func<bool> admitted, CancellationToken token)
    {
        if (!Enum.IsDefined(service)) { throw new ArgumentOutOfRangeException(nameof(service)); }
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Provider refresh requires current private host admission."); }
        if (service == ProviderService.Copilot) { await copilot.RefreshAsync(admitted, token).ConfigureAwait(false); }
        return await ObserveAsync(token).ConfigureAwait(false);
    }

    public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        var baseline = await local.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var hosted = await copilot.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return new(baseline.Installation, baseline.Catalogues.Select(item =>
            item.Service == ProviderService.Copilot ? hosted : item).ToArray());
    }
}
