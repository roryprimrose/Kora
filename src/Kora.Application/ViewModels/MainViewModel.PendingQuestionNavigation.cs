using Kora.Application.Diagnostics;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private IPendingQuestionNavigation? pendingQuestionNavigation;
    private long pendingQuestionNavigationRevision;
    private string pendingQuestionNavigationStatus = string.Empty;
    public string PendingQuestionNavigationStatus
    {
        get => pendingQuestionNavigationStatus;
        private set => SetProperty(ref pendingQuestionNavigationStatus, value);
    }

    public void BindPendingQuestionNavigation(IPendingQuestionNavigation? navigation)
    {
        pendingQuestionNavigation = navigation;
        Interlocked.Increment(ref pendingQuestionNavigationRevision);
    }

    private sealed record PendingQuestionCapture(IPendingQuestionNavigation? Presenter, IPendingQuestionFocus? Focus,
        PendingQuestionCommand? Command, long NameRevision, long InputRevision, long OwnerBinding,
        long PresenterBinding, string Composer);

    private PendingQuestionCapture CapturePendingQuestion(string text, bool beforeRouting = false)
    {
        var presenter = pendingQuestionNavigation;
        var presenterBinding = Interlocked.Read(ref pendingQuestionNavigationRevision);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var inputRevision = Interlocked.Read(ref workspaceSurfaceInputRevision) + (beforeRouting ? 1 : 0);
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var composer = CommandText;
        var command = PendingQuestionCommand.Parse(text, AssistantName);
        IPendingQuestionFocus? focus = null;
        try
        {
            if (command is { SessionId: { } session, TaskId: { } task })
            { focus = presenter?.CaptureFocus(session, task); }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Capturing the exact pending question focus");
        }
        return new(presenter, focus, command, nameRevision, inputRevision, ownerBinding, presenterBinding, composer);
    }

    private async Task ExecutePendingQuestionCommand(PendingQuestionCommand command, string text,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput, PendingQuestionCapture? originalTarget)
    {
        const string unavailable = "Pending question navigation unavailable. Only the exact still-live original native question can be focused; no answer or recovery was performed.";
        var origin = OriginalOrigin(initiator);
        if (initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !isAssistantNameAvailable
            || origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(text, AssistantName))
        {
            PendingQuestionNavigationStatus = unavailable;
            return;
        }
        if (command.IsHelp || command.SessionId is null || command.TaskId is null)
        {
            PendingQuestionNavigationStatus = PendingQuestionCommand.Help;
            return;
        }
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var target = originalTarget ?? CapturePendingQuestion(text);
        var active = true;
        bool Admitted() => active && input.Origin == origin && input.NavigationInitiallyAdmitted && input.Eligible()
            && !IsProtectedCall && input.Revision == CallPolicyRevision && CanRevealPrivatePresentation
            && input.NavigationOwnerRevision == auditImpactOwnerRevision()
            && target.OwnerBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && target.InputRevision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && target.NameRevision == assistantNameConfiguration.Get().Revision
            && string.Equals(target.Composer, CommandText, StringComparison.Ordinal)
            && target.PresenterBinding == Interlocked.Read(ref pendingQuestionNavigationRevision)
            && ReferenceEquals(target.Presenter, pendingQuestionNavigation);
        if (!Admitted() || target.Focus is null)
        {
            PendingQuestionNavigationStatus = unavailable;
            return;
        }
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        try
        {
            await uiDispatcher.InvokeAsync(async () =>
            {
                using var activity = continuation();
                try
                {
                    var focused = Admitted() && await target.Focus.FocusAsync(Admitted) && Admitted();
                    PendingQuestionNavigationStatus = focused
                        ? "Exact pending question focused. Answer separately using its existing native controls."
                        : unavailable;
                    activity.Complete(focused ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
                }
                catch
                {
                    activity.Complete(HostOperationOutcome.Failed);
                    throw;
                }
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Focusing the exact pending question");
            PendingQuestionNavigationStatus = unavailable;
        }
        finally { active = false; }
    }
}
