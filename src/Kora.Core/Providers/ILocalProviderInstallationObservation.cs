namespace Kora.Core.Providers;

/// <summary>Bounded supported-installation metadata observation without endpoint or process execution.</summary>
public interface ILocalProviderInstallationObservation
{
    /// <summary>Observes installation only; unknown state fails closed.</summary>
    Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken);
}
