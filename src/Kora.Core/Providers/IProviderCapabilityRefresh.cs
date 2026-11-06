namespace Kora.Core.Providers;

/// <summary>Explicit read-only refresh under live host admission; never signs in or acquires models.</summary>
public interface IProviderCapabilityRefresh
{
    Task<ProviderCatalogueObservation> RefreshAsync(ProviderService service, Func<bool> admitted, CancellationToken token);
}
