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
        services.AddSingleton<IWindowsOllamaQualificationSource, UnqualifiedOllamaQualificationSource>();
        services.AddSingleton<WindowsOllamaRuntimeResolver>();
        services.AddSingleton<ICopilotDeploymentApprovalSource, UnavailableCopilotDeploymentApprovalSource>();
        services.AddSingleton<CopilotAccountBroker>();
        services.AddSingleton<ICopilotExecutionAccountSource>(provider => provider.GetRequiredService<CopilotAccountBroker>());
        services.AddSingleton<CopilotSdkTurnRuntime>();
        services.AddSingleton<Kora.Application.Dependencies.HostedTransmissionReviewService>();
        services.AddSingleton<IProviderCapabilityCatalogue, CopilotProviderCatalogue>();
        services.AddSingleton<CopilotModelProviderFactory>();
        services.AddSingleton<OllamaModelProviderFactory>();
        services.AddSingleton<ILocalProviderInstallationObservation, WindowsOllamaInstallationObservation>();
        services.AddSingleton<IProviderProfilePreferences>(provider =>
            new LocalProviderProfilePreferences(provider.GetRequiredService<IPreferenceStore>()));
    }
}
