using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<SessionListPreparation, Task<bool>>? SessionListPreparationRequested;

    private async Task ExecuteSessionListPreparation(SessionListPreparationCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        if (!command.IsValid)
        {
            ShowSessionListPreparation("Session list preparation not accepted.", SessionListPreparationCommand.Syntax);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var captured = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var owner = captured.NavigationOwnerRevision;
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var name = AssistantName;
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var preparationRevision = Interlocked.Read(ref approvalPreparationRevision);
        var sourceText = CommandText;
        var live = true;
        bool Admitted() => live && !disposed && captured.NavigationInitiallyAdmitted && captured.Eligible()
            && captured.Origin == origin && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && !IsProtectedCall && CallPolicyRevision == captured.Revision && owner == auditImpactOwnerRevision()
            && ownerBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && preparationRevision == Interlocked.Read(ref approvalPreparationRevision)
            && revision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && string.Equals(sourceText, CommandText, StringComparison.Ordinal)
            && string.Equals(name, AssistantName, StringComparison.Ordinal)
            && nameRevision == assistantNameConfiguration.Get().Revision
            && (!command.IsActivated || isAssistantNameAvailable)
            && (origin != RequestOrigin.ActivatedVoice || command.IsActivated && voiceOwnershipEligible())
            && !IsResponseInteractionPending && !IsGrantEditorVisible && !HasResponseActions;
        try
        {
            if (!Admitted())
            {
                PresentSessionListPreparation(false);
                return;
            }
            if (command.IsHelp)
            {
                ShowSessionListPreparation("Session list preparation help.", SessionListPreparationCommand.Syntax);
                return;
            }
            var handler = SessionListPreparationRequested;
            if (handler is null) { PresentSessionListPreparation(false); return; }
            var prepared = false;
            await HostRequestRunner.RunAsync(origin, async () =>
            {
                using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Presentation);
                try
                {
                    var result = await handler(new(new(command.SessionId!.Value), Admitted));
                    prepared = result && Admitted();
                    activity.Complete(prepared ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
                }
                catch { activity.Complete(HostOperationOutcome.Failed); throw; }
            });
            PresentSessionListPreparation(prepared);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Preparing the native session metadata query");
            PresentSessionListPreparation(false);
        }
        finally { live = false; }
    }

    private void PresentSessionListPreparation(bool prepared)
    {
        if (disposed || !IsHostInputEligible) { return; }
        ShowSessionListPreparation(prepared ? "Session list PREPARED / REVIEW NEEDED" : "Session list preparation unavailable / cancelled",
            (prepared ? "The native ExactId query is prepared and focused. No Search has run. "
                : "Preparation was not confirmed. Review native drafts and private admission, then issue a fresh command. No Search has run. ")
            + SessionListPreparationCommand.Syntax);
    }

    private void ShowSessionListPreparation(string title, string body)
    {
        if (IsResponseInteractionPending || IsGrantEditorVisible || HasResponseActions)
        {
            Transcript = "Session list preparation unavailable: finish the existing question, approval or security review explicitly first. Nothing was searched or answered.";
            return;
        }
        ShowInformation(title, body);
    }
}
