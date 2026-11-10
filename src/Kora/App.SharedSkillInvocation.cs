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
            Services.GetRequiredService<ILogger<SharedSkillInvocationController>>(),
            ActivatorUtilities.CreateInstance<ReviewedToolSkillInvocationWorkflow>(Services), questionWindow);
        sharedSkillInvocation.Bind();
        var responses = responseWindow ?? throw new InvalidOperationException("Shared skill invocation requires the owning response workbench.");
        sharedSkillInvocation.BindWorkbench(responses.PrepareWorkbenchOwner, view =>
        {
            var owner = responses.PrepareWorkbenchOwner();
            owner.ShowSessions(expanded: true);
            owner.ShowNativeReview(view, "Reviewed shared skill invocation", view,
                () => main.CanRevealPrivatePresentation, _ => { });
        });
        BindOwnedSkillAuthoring(main);
    }
}
