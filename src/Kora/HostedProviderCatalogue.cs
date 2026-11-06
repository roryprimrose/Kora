using Kora.Core.Providers;
using Kora.Windows.Dependencies;

namespace Kora;

internal sealed class HostedProviderCatalogue(CopilotProviderCatalogue existing, ClaudeAccountBroker claude)
    : IProviderCapabilityCatalogue, IProviderCapabilityRefresh
{
    public async Task<ProviderCatalogueObservation> RefreshAsync(ProviderService service, Func<bool> admitted, CancellationToken token)
    {
        if (!Enum.IsDefined(service)) { throw new ArgumentOutOfRangeException(nameof(service)); }
        if (!admitted()) { throw new InvalidOperationException("Provider refresh requires current private host admission."); }
        if (service == ProviderService.Claude) { await claude.RefreshAsync(admitted, token).ConfigureAwait(false); }
        else { await existing.RefreshAsync(service, admitted, token).ConfigureAwait(false); }
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Provider refresh lost private host admission."); }
        return await ObserveAsync(token).ConfigureAwait(false);
    }

    public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        var baseline = await existing.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await claude.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return new(baseline.Installation, baseline.Catalogues.Select(item =>
            item.Service == ProviderService.Claude ? snapshot : item).ToArray());
    }
}
