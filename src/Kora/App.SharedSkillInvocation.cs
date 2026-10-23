using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private SharedSkillInvocationController? sharedSkillInvocation;
    private void BindSharedSkillInvocation(MainViewModel main, SkillPackagesWindowController sources)
    {
        sharedSkillInvocation = new(main, Services.GetRequiredService<SharedSkillInvocationWorkflow>(), sources,
            () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
            () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision,
            Services.GetRequiredService<ILogger<SharedSkillInvocationController>>());
        sharedSkillInvocation.Bind();
    }
}
