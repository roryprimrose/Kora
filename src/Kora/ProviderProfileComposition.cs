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
        services.AddSingleton<DevelopmentPreviewAdmission>(provider => new(DevelopmentPreviewBuild.Enabled,
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<Kora.Core.Auditing.ISecurityAuditLog>()));
        services.AddSingleton<DevelopmentOllamaAdmissionSource>();
        services.AddSingleton<DevelopmentClaudeDeploymentSource>();
        services.AddSingleton<ILocalModelAdmissionBroker>(provider => provider.GetRequiredService<WindowsOllamaAdmissionBroker>());
        services.AddSingleton<LocalModelAdmissionPreferences>();
        services.AddSingleton<OllamaAdmissionWorkflow>();
        services.AddSingleton<IWindowsOllamaQualificationSource>(provider => DevelopmentPreviewBuild.Enabled
            ? provider.GetRequiredService<DevelopmentOllamaAdmissionSource>()
            : provider.GetRequiredService<WindowsOllamaAdmissionBroker>());
        services.AddSingleton<WindowsOllamaRuntimeResolver>();
        services.AddSingleton<CopilotEvaluationDeploymentSource>();
        services.AddSingleton<ICopilotDeploymentApprovalSource>(provider => provider.GetRequiredService<CopilotEvaluationDeploymentSource>());
        services.AddSingleton<CopilotAccountBroker>();
        services.AddSingleton<ICopilotExecutionAccountSource>(provider => provider.GetRequiredService<CopilotAccountBroker>());
        services.AddSingleton<CopilotSdkTurnRuntime>();
        services.AddSingleton<Kora.Application.Dependencies.HostedTransmissionReviewService>();
        if (DevelopmentPreviewBuild.Enabled)
        { services.AddSingleton<IClaudeDeploymentApprovalSource>(provider => provider.GetRequiredService<DevelopmentClaudeDeploymentSource>()); }
        else { services.AddSingleton<IClaudeDeploymentApprovalSource, UnavailableClaudeDeploymentApprovalSource>(); }
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