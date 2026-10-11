using Kora.Core.Providers;

namespace Kora.Application.Configuration;

/// <summary>The composed services have no qualified capability/authentication adapters. No models are invented.</summary>
public sealed class UnavailableProviderCapabilityCatalogue(ILocalProviderInstallationObservation installation, TimeProvider time)
    : IProviderCapabilityCatalogue
{
    public async Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        var observed = await installation.ObserveAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var now = time.GetUtcNow();
        return new(observed, Array.AsReadOnly(new[]
        {
            ProviderService.Local, ProviderService.Copilot, ProviderService.Claude, ProviderService.Codex,
        }.Select(service => new ProviderCapabilityCatalogue(service, ProviderCatalogueStatus.Unavailable, null,
            new(1), now, now.AddMinutes(5), [])).ToArray()));
    }
}
