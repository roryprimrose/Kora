using Kora.Application.Configuration;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Windows.Dependencies;
using Kora.Windows.Storage;
using Kora.Definitions.Qualifications;
using Kora.Application.Dependencies;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

internal static class ProviderProfileComposition
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton<ProviderProfileConfigurationService>();
        services.AddSingleton<SessionProviderProfileConfigurationService>();
        services.AddSingleton<ISessionProviderProfileStore>(provider => provider.GetRequiredService<WindowsSqliteHostInteractionStore>());
        services.AddSingleton<ILocalModelQualificationAuthority, BundledLocalModelQualificationAuthority>();
        services.AddSingleton<IWindowsOllamaAdmissionObservation, WindowsOllamaAdmissionObservation>();
        services.AddSingleton<WindowsOllamaAdmissionBroker>();
        services.AddSingleton<ILocalModelAdmissionBroker>(provider => provider.GetRequiredService<WindowsOllamaAdmissionBroker>());
        services.AddSingleton<LocalModelAdmissionPreferences>();
        services.AddSingleton<OllamaAdmissionWorkflow>();
        services.AddSingleton<IWindowsOllamaQualificationSource>(provider => provider.GetRequiredService<WindowsOllamaAdmissionBroker>());
        services.AddSingleton<WindowsOllamaRuntimeResolver>();
        services.AddSingleton<ICopilotDeploymentApprovalSource, UnavailableCopilotDeploymentApprovalSource>();
        services.AddSingleton<CopilotAccountBroker>();
        services.AddSingleton<ICopilotExecutionAccountSource>(provider => provider.GetRequiredService<CopilotAccountBroker>());
        services.AddSingleton<CopilotSdkTurnRuntime>();
        services.AddSingleton<Kora.Application.Dependencies.HostedTransmissionReviewService>();
        services.AddSingleton<IClaudeDeploymentApprovalSource, UnavailableClaudeDeploymentApprovalSource>();
        services.AddSingleton<ClaudeAccountBroker>();
        services.AddSingleton<ClaudeMessagesRuntime>();
        services.AddSingleton<ClaudeModelProviderFactory>();
        services.AddSingleton<HostedModelProviderFactory>();
        services.AddSingleton<CopilotProviderCatalogue>();
        services.AddSingleton<IProviderCapabilityCatalogue, HostedProviderCatalogue>();
        services.AddSingleton<CopilotModelProviderFactory>();
        services.AddSingleton<OllamaModelProviderFactory>();
        services.AddSingleton<ILocalProviderInstallationObservation, WindowsOllamaInstallationObservation>();
        services.AddSingleton<IProviderProfilePreferences>(provider =>
            new LocalProviderProfilePreferences(provider.GetRequiredService<IPreferenceStore>()));
    }
}