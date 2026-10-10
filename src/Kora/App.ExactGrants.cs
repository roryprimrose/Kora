using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Storage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private ExactGrantsWindowController? exactGrants;

    private void BindExactGrants(MainViewModel main) =>
        exactGrants = new(main, Services.GetRequiredService<IExactGrantStore>(),
            Services.GetRequiredService<ExactGrantControlAdmission>(), Services.GetRequiredService<ISessionWorkspaceAccess>(),
            () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
            Services.GetRequiredService<ILogger<ExactGrantsViewModel>>());
}
