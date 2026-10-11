using Kora.Application.Configuration;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Windows.Dependencies;
using Kora.Windows.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

internal static class ProviderProfileComposition
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton<ProviderProfileConfigurationService>();
        services.AddSingleton<SessionProviderProfileConfigurationService>();
        services.AddSingleton<ISessionProviderProfileStore>(provider => provider.GetRequiredService<WindowsSqliteHostInteractionStore>());
        services.AddSingleton<IProviderCapabilityCatalogue, UnavailableProviderCapabilityCatalogue>();
        services.AddSingleton<ILocalProviderInstallationObservation, WindowsOllamaInstallationObservation>();
        services.AddSingleton<IProviderProfilePreferences>(provider =>
            new LocalProviderProfilePreferences(provider.GetRequiredService<IPreferenceStore>()));
    }
}
