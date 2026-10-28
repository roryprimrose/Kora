using Kora.Application.Configuration;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Storage;
using Kora.SettingInspection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private static void BindSettingInspection(SettingsWindowController controller)
    {
        var source = new ConfirmedSettingsSource(Services.GetRequiredService<AppearanceConfigurationService>(),
            Services.GetRequiredService<SpeechConfigurationService>(), Services.GetRequiredService<PlaybackVolumeConfigurationService>(),
            Services.GetRequiredService<ResponseModeConfigurationService>());
        var workflow = new SettingInspectionWorkflow(source,
            new SettingInspectionConversation(Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<SessionConversationService>(), Services.GetRequiredService<IModelTurnExecution>()),
            Services.GetRequiredService<ISessionWorkspaceAccess>(), Services.GetRequiredService<ISecurityAuditLog>(),
            Services.GetRequiredService<TimeProvider>(), Services.GetRequiredService<ILogger<SettingInspectionWorkflow>>());
        controller.BindSettingInspection(workflow, Services.GetRequiredService<TimeProvider>());
    }
}
