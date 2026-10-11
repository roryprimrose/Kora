namespace Kora.Core.Providers;

/// <summary>Read-only host-normalized catalogue observation. Implementations cannot authenticate or acquire models.</summary>
public interface IProviderCapabilityCatalogue
{
    /// <summary>Observes installation and current independent service capability snapshots.</summary>
    Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken);
}
