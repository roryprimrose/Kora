using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private CustomizationDiscoveryController? customizationDiscovery;

    private void BindCustomizationDiscovery(MainViewModel main)
    {
        var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>();
        customizationDiscovery = new(main, Services.GetRequiredService<CustomizationDiscoveryService>(),
            Services.GetRequiredService<SessionWorkspaceService>(), Services.GetRequiredService<SessionConversationService>(),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<CustomizationDiscoveryController>>());
        customizationDiscovery.Bind();
    }
}
