using Kora.Core.Providers;

namespace Kora.Tools.Providers;

public sealed class ProviderRefresh
{
    internal async Task<ProviderCatalogueObservation> ExecuteAsync(IProviderCapabilityCatalogue catalogue,
        ProviderService service, Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Provider refresh requires current private host admission."); }
        if (catalogue is not IProviderCapabilityRefresh refresh)
        { throw new InvalidOperationException("This service cannot refresh its model catalogue. Existing selections were not changed."); }
        var observation = await refresh.RefreshAsync(service, admitted, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Provider refresh lost private host admission."); }
        return observation;
    }
}
