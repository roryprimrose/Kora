using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<WorkspaceSurfaceNavigation, Task>? WorkspaceSurfaceRequested;
    private long workspaceSurfaceInputRevision;

    private async Task ExecuteWorkspaceSurfaceCommand(
        WorkspaceSurfaceCommand command, string input, SecurityAuditInitiator initiator,
        ManualCallInput? originalInput)
    {
        if (command.Target == WorkspaceSurfaceTarget.Invalid)
        {
            ReportHostInteractionFailure("Navigation command not accepted. Use exactly: "
                + string.Join(" | ", WorkspaceSurfaceCommand.FixedPhrases) + ". No target or decision changed.");
            return;
        }
        if ((!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(input, AssistantName))
            || (initiator == SecurityAuditInitiator.VoiceCommand
                && !commandRouter.IsActivationPrefixed(input, AssistantName)))
        {
            ReportHostInteractionFailure("Navigation unavailable: activated voice requires the current available assistant-name prefix.");
            return;
        }
        var origin = originalInput?.Origin ?? OriginalOrigin(initiator);
        var captured = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var sourceText = CommandText;
        var owner = captured.NavigationOwnerRevision;
        var name = AssistantName;
        var initiallyAdmitted = captured.NavigationInitiallyAdmitted && captured.Eligible() && !IsProtectedCall;
        bool WaitingInputCurrent() => captured.AssistantNameRevision == assistantNameConfiguration.Get().Revision
            && captured.OwnershipBindingRevision == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && captured.InputRevision == (originalInput is null ? revision : revision - 1);
        bool Admitted() => initiallyAdmitted && captured.Eligible() && !IsProtectedCall
            && (command.Target != WorkspaceSurfaceTarget.OpenNamedConversation || CanEnterRetainedAnswerRead)
            && (command.Target != WorkspaceSurfaceTarget.WaitingQuestions || WaitingInputCurrent())
            && captured.Revision == CallPolicyRevision && owner == auditImpactOwnerRevision()
            && revision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && string.Equals(sourceText, CommandText, StringComparison.Ordinal)
            && string.Equals(name, AssistantName, StringComparison.Ordinal);
        if (!Admitted() || WorkspaceSurfaceRequested is not { } handler)
        {
            ReportHostInteractionFailure("Navigation unavailable: the native surface route or current private host admission is unavailable. No window or session was created.");
            return;
        }
        if (command.Target == WorkspaceSurfaceTarget.WaitingQuestions)
        {
            await ReadWaitingQuestionsAsync(handler, Admitted, origin);
            return;
        }
        await HostRequestRunner.RunAsync(origin, () => handler(new(command.Target, Admitted, command.SessionPosition,
            command.ConversationName, origin)));
    }

    private async Task ReadWaitingQuestionsAsync(Func<WorkspaceSurfaceNavigation, Task> handler,
        Func<bool> admitted, Kora.Core.Hosting.RequestOrigin origin)
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, origin);
        using var cancellation = new CancellationTokenSource();
        void CheckCurrency(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!admitted()) { cancellation.Cancel(); }
        }
        void ClosePrivacy(object? sender, EventArgs args) => cancellation.Cancel();
        PropertyChanged += CheckCurrency;
        PrivacyClosureRequested += ClosePrivacy;
        var navigation = new WorkspaceSurfaceNavigation(WorkspaceSurfaceTarget.WaitingQuestions, admitted, Origin: origin)
        {
            Request = activity.Request,
            CancellationToken = cancellation.Token,
        };
        try
        {
            await handler(navigation);
            if (!admitted() || cancellation.IsCancellationRequested)
            {
                navigation.Outcome = WorkspaceSurfaceNavigationOutcome.Cancelled;
            }
            activity.Complete(navigation.Outcome switch
            {
                WorkspaceSurfaceNavigationOutcome.Completed => HostOperationOutcome.Completed,
                WorkspaceSurfaceNavigationOutcome.Cancelled => HostOperationOutcome.Cancelled,
                _ => HostOperationOutcome.Failed,
            });
            if (navigation.Outcome != WorkspaceSurfaceNavigationOutcome.Completed)
            {
                ReportHostInteractionFailure(navigation.Outcome == WorkspaceSurfaceNavigationOutcome.Cancelled
                    ? "Waiting questions cancelled: the original input or private source changed. No late page is current."
                    : "Waiting questions unavailable: no fresh bounded observation completed. Use Needs answer and Refresh when the native reader is available.");
            }
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            PropertyChanged -= CheckCurrency;
            PrivacyClosureRequested -= ClosePrivacy;
        }
    }
}
