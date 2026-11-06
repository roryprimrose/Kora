using Kora.Application.Dependencies;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Storage;
using Kora.Core.Tools;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

internal static class RemainingWorkManagementComposition
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddKeyedSingleton<ModelTurnHost>("remaining-work-management", (provider, _) => new ModelTurnHost(
            provider.GetRequiredService<ISessionWorkspaceStore>(),
            provider.GetRequiredService<ISessionWorkspaceAccess>(),
            provider.GetRequiredService<ICapabilityHostAccess>(),
            provider.GetRequiredService<ReadOnlyCapabilityRegistry>(),
            provider.GetRequiredService<ISecurityAuditLog>(),
            provider.GetRequiredService<ILogger<ModelTurnHost>>(),
            provider.GetRequiredService<TimeProvider>(),
            [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null)],
            provider.GetRequiredService<ProviderModeConfigurationService>()));
        services.AddSingleton(provider =>
        {
            var host = provider.GetRequiredKeyedService<ModelTurnHost>("remaining-work-management");
            var workspace = provider.GetRequiredService<SessionWorkspaceService>();
            var access = provider.GetRequiredService<ISessionWorkspaceAccess>();
            var time = provider.GetRequiredService<TimeProvider>();
            var answers = new SessionConversationService(
                provider.GetRequiredService<ISessionConversationStore>(),
                provider.GetRequiredService<ISessionWorkspaceStore>(), workspace, access, host, time,
                provider.GetRequiredService<ILogger<SessionConversationService>>(), execution: host);
            return new RemainingWorkManagement(provider.GetRequiredService<SessionConversationService>(), answers,
                workspace, access, time, provider.GetRequiredService<ILogger<RemainingWorkManagement>>());
        });
    }
}