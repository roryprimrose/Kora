using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<Func<bool>, Task<bool>>? NewRequestOpeningRequested;

    private Func<bool> CaptureNewRequestOpeningEligibility(ManualCallInput input)
    {
        var owner = input.NavigationOwnerRevision;
        var name = input.AssistantNameRevision;
        var decisionFree = !IsResponseInteractionPending;
        return () => decisionFree && input.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && input.Eligible() && CallPolicyRevision == input.Revision
            && auditImpactOwnerRevision() == owner && assistantNameConfiguration.Get().Revision == name
            && !IsResponseInteractionPending
            && (input.Origin != RequestOrigin.ActivatedVoice || CallObservation.AllowActivation);
    }

    private async Task ExecuteNewRequestOpenCommand(NewRequestOpenCommand command,
        SecurityAuditInitiator initiator, Func<bool>? capturedEligibility)
    {
        if (IsResponseInteractionPending)
        {
            Transcript = "New request opening blocked: resolve the current question or approval explicitly first. The complete pending decision is unchanged.";
            return;
        }
        if (!command.Accepted || commandRouter.Match(NewRequestOpenCommand.Phrase, AssistantName).IsMatch
            || string.Equals(AssistantName, NewRequestOpenCommand.Phrase, StringComparison.OrdinalIgnoreCase))
        {
            Transcript = "New request opening not accepted. " + NewRequestOpenCommand.Syntax
                + " A conflicting current assistant name must be changed explicitly.";
            return;
        }
        var origin = OriginalOrigin(initiator);
        if (!isAssistantNameAvailable || origin == RequestOrigin.ActivatedVoice && !command.ActivationPrefixed)
        {
            Transcript = "New request opening unavailable: voice requires the available current assistant name prefix.";
            return;
        }
        var eligible = capturedEligibility ?? CaptureNewRequestOpeningEligibility(
            CaptureManualCallInput(origin, CallPolicyRevision));
        if (!eligible())
        {
            Transcript = "New request opening denied: original input, privacy, ownership or call admission changed. Nothing is queued for later.";
            return;
        }
        if (NewRequestOpeningRequested is not { } open)
        {
            Transcript = "New request opening unavailable: the existing private native composer is not bound. Model navigation is not registered.";
            return;
        }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        try
        {
            var opened = await open(eligible);
            if (!eligible()) { activity.Complete(HostOperationOutcome.Cancelled); return; }
            Transcript = opened
                ? "New request composer activated only. Existing memory and review state are unchanged; Create and Submit require their own native gestures."
                : "New request opening not confirmed. No target, Submit or delayed opening was requested.";
            activity.Complete(opened ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            ApplicationLog.Error(logger, exception, "Opening the existing native New request composer");
            if (!eligible()) { return; }
            Transcript = "New request opening failed. Existing memory was not submitted; use the native New request entry to recover.";
        }
    }
}
