using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Tools.Providers;

public sealed class ProviderSettingsGet
{
    internal ProviderSettingsSnapshot Execute(ProviderProfilePreferences? saved, ProviderCatalogueObservation observation, DateTimeOffset now)
    {
        var desired = saved ?? new ProviderProfilePreferences(
            ProviderProfileRules.InitialService(observation.Installation), []);
        var tuple = desired.For(desired.Service);
        var effective = desired.Service != ProviderService.Unset
            && (desired.Service != ProviderService.Local || observation.Installation == LocalProviderInstallation.Installed)
            && ProviderProfileRules.Resolve(tuple, observation.For(desired.Service), now) is not null ? tuple : null;
        return new(desired, saved is not null, effective, observation.Installation,
            observation.Catalogues.Select(catalogue => Availability(catalogue, observation.Installation, now)).ToArray());
    }

    internal static ProviderServiceAvailability Availability(ProviderCapabilityCatalogue catalogue,
        LocalProviderInstallation installation, DateTimeOffset now)
    {
        var current = catalogue.IsCurrent(now)
            && (catalogue.Service != ProviderService.Local || installation == LocalProviderInstallation.Installed);
        return new(catalogue.Service, current ? ProviderCatalogueStatus.Current
                : catalogue.Status == ProviderCatalogueStatus.AuthenticationRequired ? catalogue.Status : ProviderCatalogueStatus.Unavailable,
            catalogue.Account, catalogue.Revision, catalogue.ValidUntil, current ? catalogue.Models.Count : 0);
    }
}
