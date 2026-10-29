using Kora.Windows.Skills.RuntimeSetup;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class App
{
    private void BindBundledRuntimeSetup() => skillPackagesWindow!.BindRuntimeSetup(
        Services.GetRequiredService<WindowsBundledRuntimeSetup>(),
        () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision);
}
