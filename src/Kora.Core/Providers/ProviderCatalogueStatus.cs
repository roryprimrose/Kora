namespace Kora.Core.Providers;

/// <summary>Truthful capability availability, not egress or execution permission.</summary>
public enum ProviderCatalogueStatus
{
    /// <summary>No qualified catalogue mechanism is available.</summary>
    Unavailable,
    /// <summary>The service requires independent explicit authentication.</summary>
    AuthenticationRequired,
    /// <summary>A current account-bound catalogue is available.</summary>
    Current,
}
