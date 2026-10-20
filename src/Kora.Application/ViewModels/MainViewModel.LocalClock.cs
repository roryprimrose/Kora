using Kora.Application.Clock;
using Kora.Core.Auditing;
using Kora.Core.Clock;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly LocalClockWorkflow? localClock;

    private bool IsLocalClockPresentationClear => !IsResponseInteractionPending && !IsGrantEditorVisible
        && pendingPowerAction is null && responseReadDecisionsEligible();

    private void ExecuteLocalClockCommand(LocalClockCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        bool Eligible() => origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && initiator is SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand
            && input.Origin == origin && input.Eligible() && CallPolicyRevision == input.Revision
            && isAssistantNameAvailable && assistantNameConfiguration.Get().Revision == input.AssistantNameRevision
            && settingsOwnershipRevision() == input.NavigationOwnerRevision
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == input.OwnershipBindingRevision
            && Interlocked.Read(ref workspaceSurfaceInputRevision) == revision
            && IsLocalClockPresentationClear && !IsProtectedCall
            && (origin != RequestOrigin.ActivatedVoice || command.IsActivated
                && voiceOwnershipEligible() && CallObservation.AllowActivation
                && input.InputRevision == revision - 1);
        if (!Eligible())
        {
            Transcript = "Local clock unavailable: fresh original input, owning private host, clear call policy and no pending question or security preview are required.";
            return;
        }
        if (command.Operation is LocalClockOperation.Help or LocalClockOperation.Invalid)
        {
            PresentLocalClockResponse("Local clock commands.", LocalClockCommand.Syntax);
            return;
        }
        if (localClock is null)
        {
            Transcript = "Local clock unavailable: the host clock workflow is not bound.";
            return;
        }
        var result = localClock.Execute(command.Operation, Eligible, CancellationToken.None);
        if (result.Outcome != LocalClockOutcome.Succeeded || !Eligible())
        {
            Transcript = "Local clock unavailable or retired: " + result.Outcome + ". Retry with fresh original input.";
            return;
        }
        PresentLocalClockResponse("Host-local date and time.", result.Answer!.Text);
    }

    private void PresentLocalClockResponse(string title, string body)
    {
        PresentResponse(Kora.Core.AssistantState.Information, title, body, requestWindow: false, refreshOutput: false);
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
        WindowActionRequested?.Invoke(this, WindowAction.Show);
    }
}
