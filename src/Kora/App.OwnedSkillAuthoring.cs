using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private OwnedSkillEditorController? ownedSkillEditor;
    private NamedSkillInvocationController? namedSkillInvocation;

    private void BindOwnedSkillAuthoring(MainViewModel main)
    {
        var paths = Services.GetRequiredService<IApplicationDataPaths>();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var admission = new OwnedSkillControlAdmission(Services.GetRequiredService<ISessionWorkspaceStore>(),
            Services.GetRequiredService<WindowsSqliteHostInteractionStore>(), Services.GetRequiredService<HostTaskCoordinator>());
        var workflow = new OwnedSkillSaveWorkflow(store, admission, Services.GetRequiredService<ISecurityAuditLog>());
        var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>();
        var useWorkflow = new OwnedSkillInvocationWorkflow(store, admission, Services.GetRequiredService<ISessionWorkspaceAccess>(),
            Services.GetRequiredService<ISessionWorkspaceStore>(), Services.GetRequiredService<SessionWorkspaceService>(),
            Services.GetRequiredService<SessionConversationService>(), Services.GetRequiredService<ISecurityAuditLog>(),
            Services.GetRequiredService<TimeProvider>(), Services.GetRequiredService<IArtifactUsePolicy>());
        var use = new OwnedSkillInvocationController(main, useWorkflow,
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<OwnedSkillInvocationController>>(),
            ActivatorUtilities.CreateInstance<ReviewedToolSkillInvocationWorkflow>(Services), questionWindow);
        use.Bind();
        var ownedResponses = responseWindow ?? throw new InvalidOperationException("Owned skill invocation requires the owning response workbench.");
        use.BindWorkbench(ownedResponses.PrepareWorkbenchOwner,
            view =>
            {
                var owner = ownedResponses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true, preservePresentation: true);
                owner.ShowNativeReview(view, "Review one use of a saved owned skill", view,
                    () => main.CanRevealPrivatePresentation, _ => { });
            },
            view =>
            {
                var owner = ownedResponses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true, preservePresentation: true);
                owner.ShowNativeReview(view, "Reviewed owned read-only MCP skill", view,
                    () => main.CanRevealPrivatePresentation, _ => { });
            });
        namedSkillInvocation = new(main,
            new NamedSkillCatalogue(store, workflow, Services.GetRequiredService<SharedSkillDiscoveryService>(),
                Services.GetRequiredService<ISessionWorkspaceAccess>(), Services.GetRequiredService<IArtifactUsePolicy>(),
                Services.GetRequiredService<TimeProvider>()),
            useWorkflow, Services.GetRequiredService<SharedSkillInvocationWorkflow>(),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<NamedSkillInvocationController>>(), questionWindow);
        namedSkillInvocation.Bind();
        var namedResponses = responseWindow ?? throw new InvalidOperationException("Named skill invocation requires the owning response workbench.");
        namedSkillInvocation.BindWorkbench(namedResponses.PrepareWorkbenchOwner, view =>
        {
            var owner = namedResponses.PrepareWorkbenchOwner();
            owner.ShowSessions(expanded: true);
            owner.ShowNativeReview(view, "Review instruction-only skill by readable name", view,
                () => main.CanRevealPrivatePresentation, _ => { });
        });
        var observations = new ObservationWorkflowController(main, workflow, useWorkflow,
            new(useWorkflow, admission, Services.GetRequiredService<Kora.Application.Tools.ReadOnlyCapabilityRegistry>(),
                Services.GetRequiredService<ISessionWorkspaceStore>(), Services.GetRequiredService<DependencyBootstrapper>(),
                Services.GetRequiredService<ISecurityAuditLog>(), Services.GetRequiredService<TimeProvider>(), store,
                Services.GetRequiredService<IArtifactUsePolicy>(), Services.GetRequiredService<WindowsSqliteHostInteractionStore>()),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<ObservationWorkflowController>>());
        observations.Bind();
        observations.BindWorkbench(ownedResponses.PrepareWorkbenchOwner,
            view => ownedResponses.ShowHostedView(WorkbenchActivity.ObservationEditor, view));
        ownedSkillEditor = new(main, workflow, admission,
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<OwnedSkillEditorController>>(), use,
            new(store, admission, workflow, Services.GetRequiredService<IModelContextRunner>(), Services.GetRequiredService<TimeProvider>(),
                Services.GetRequiredService<Kora.Application.Mcp.McpReadOnlyPolicy>()), observations,
            new(store, store, admission, Services.GetRequiredService<ISecurityAuditLog>(), Services.GetRequiredService<TimeProvider>()),
            new(store, admission, Services.GetRequiredService<ISecurityAuditLog>(), Services.GetRequiredService<TimeProvider>()),
            new(store, admission, Services.GetRequiredService<IModelContextRunner>(), Services.GetRequiredService<TimeProvider>()));
        ownedSkillEditor.Bind();
        var responses = responseWindow ?? throw new InvalidOperationException("Owned skill authoring requires the owning response workbench.");
        ownedSkillEditor.BindWorkbench(responses.PrepareWorkbenchOwner,
            view => responses.ShowHostedView(WorkbenchActivity.SkillEditor, view));
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.Exit += ownedSkillEditor.OnHostExit; }
    }
}
