using Kora.Application.Hosting;
using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class App
{
    private ApplicationLifecycleController? applicationLifecycle;
    private BundledFixedActionController? bundledFixedActions;

    private void BindApplicationLifecycle(MainViewModel main, NativeQuestionHost questions)
    {
        applicationLifecycle = new(main, Services.GetRequiredService<SessionConversationService>(),
            Services.GetRequiredService<SessionQueueService>(), questions,
            Services.GetRequiredService<HostTaskCoordinator>(), Services.GetRequiredService<NativeDetailRenderer>(),
            GetPresenceOwner);
        bundledFixedActions = new(main,
            skillPackagesWindow ?? throw new InvalidOperationException("The immutable package inspector is unavailable."),
            questions, applicationLifecycle,
            Services.GetRequiredService<Kora.Windows.Storage.WindowsSqliteHostInteractionStore>(),
            Services.GetRequiredService<HostTaskCoordinator>(),
            Services.GetRequiredService<Kora.Application.Interaction.HostAuthorizationService>(),
            Services.GetRequiredService<Kora.Core.Skills.IBundledScriptWorker>(),
            Services.GetRequiredService<Kora.Tools.SessionControl.FixedSessionActionBroker>(),
            Services.GetRequiredService<NativeDetailRenderer>(), GetPresenceOwner,
            () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsReady
                && !Services.GetRequiredService<DesktopInstanceOwnershipBridge>().IsHandoffRecoveryRequired,
            () => Services.GetRequiredService<DesktopInstanceOwnershipBridge>().AdmissionRevision,
            Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BundledFixedActionController>>());
    }
}