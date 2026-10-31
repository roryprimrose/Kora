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
            Services.GetRequiredService<TimeProvider>());
        var use = new OwnedSkillInvocationController(main, useWorkflow,
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<OwnedSkillInvocationController>>(),
            ActivatorUtilities.CreateInstance<ReviewedToolSkillInvocationWorkflow>(Services), questionWindow);
        use.Bind();
        namedSkillInvocation = new(main,
            new NamedSkillCatalogue(store, workflow, Services.GetRequiredService<SharedSkillDiscoveryService>(),
                Services.GetRequiredService<ISessionWorkspaceAccess>(), Services.GetRequiredService<IArtifactUsePolicy>(),
                Services.GetRequiredService<TimeProvider>()),
            useWorkflow, Services.GetRequiredService<SharedSkillInvocationWorkflow>(),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<NamedSkillInvocationController>>(), questionWindow);
        namedSkillInvocation.Bind();
        var observations = new ObservationWorkflowController(main, workflow, useWorkflow,
            new(useWorkflow, admission, Services.GetRequiredService<Kora.Application.Tools.ReadOnlyCapabilityRegistry>(),
                Services.GetRequiredService<ISessionWorkspaceStore>(), Services.GetRequiredService<DependencyBootstrapper>(),
                Services.GetRequiredService<ISecurityAuditLog>(), Services.GetRequiredService<TimeProvider>(), store,
                Services.GetRequiredService<IArtifactUsePolicy>(), Services.GetRequiredService<WindowsSqliteHostInteractionStore>()),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<ObservationWorkflowController>>());
        observations.Bind();
        ownedSkillEditor = new(main, workflow, admission,
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<OwnedSkillEditorController>>(), use,
            new(store, admission, workflow, Services.GetRequiredService<IModelContextRunner>(), Services.GetRequiredService<TimeProvider>()), observations);
        ownedSkillEditor.Bind();
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.Exit += ownedSkillEditor.OnHostExit; }
    }
}
