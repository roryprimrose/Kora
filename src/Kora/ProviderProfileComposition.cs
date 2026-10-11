using Kora.Application.Configuration;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

internal static class ProviderProfileComposition
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton<ProviderProfileConfigurationService>();
        services.AddSingleton<IProviderCapabilityCatalogue, UnavailableProviderCapabilityCatalogue>();
        services.AddSingleton<ILocalProviderInstallationObservation, WindowsOllamaInstallationObservation>();
        services.AddSingleton<IProviderProfilePreferences>(provider =>
            new LocalProviderProfilePreferences(provider.GetRequiredService<IPreferenceStore>()));
    }
}
