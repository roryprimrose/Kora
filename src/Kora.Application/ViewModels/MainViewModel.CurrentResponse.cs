using Kora.Application.Diagnostics;
using Kora.Application.Presentation;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record FinalizedModelAnswer(string Body);

    private ICurrentResponsePresentation? currentResponsePresentation;
    private long currentResponseBindingRevision;
    private sealed record CurrentResponseInput(CurrentResponseSpeech? Source, Func<bool> Eligible);

    public void BindCurrentResponsePresentation(ICurrentResponsePresentation? presentation)
    {
        currentResponsePresentation = presentation;
        Interlocked.Increment(ref currentResponseBindingRevision);
    }

    private bool CanActOnCurrentResponse => IsCallMutationHostEligible && !IsProtectedCall
        && !IsBusy && !IsResponseInteractionPending && !IsGrantEditorVisible && !HasResponseActions
        && responseReadDecisionsEligible() && ClipboardPreview is null
        && FileReview is null && FolderReview is null && FileRevision is null && FolderRevision is null
        && providerFieldPreview is null && auditImpactPreview?.Get().ProposedDays is null
        && diagnosticImpactPreview?.Get().ProposedDays is null && sessionRetentionPlanner?.Get().Proposed is null;

    private CurrentResponseInput CaptureCurrentResponseInput(ManualCallInput input, long expectedInputRevision)
    {
        var source = Volatile.Read(ref ordinaryResponse);
        var owner = auditImpactOwnerRevision();
        var name = assistantNameConfiguration.Get().Revision;
        var text = CommandText;
        var initiallyAdmitted = CanActOnCurrentResponse && input.Eligible();
        return new(source, () => initiallyAdmitted && CanActOnCurrentResponse && input.Eligible()
            && input.Revision == CallPolicyRevision && auditImpactOwnerRevision() == owner
            && assistantNameConfiguration.Get().Revision == name
            && string.Equals(text, CommandText, StringComparison.Ordinal)
            && Interlocked.Read(ref workspaceSurfaceInputRevision) == expectedInputRevision);
    }

    public Task DismissCurrentResponseAsync() => RunCurrentResponseUiAsync(CurrentResponseAction.Dismiss);
    public Task OpenCurrentResponseDetailsAsync() => RunCurrentResponseUiAsync(CurrentResponseAction.Details);

    private Task RunCurrentResponseUiAsync(CurrentResponseAction action)
    {
        if (HostActivity.HasScope && HostActivity.Current?.Request.Origin != RequestOrigin.LocalUi)
        {
            Transcript = "Current response action unavailable: this is not an original native UI request.";
            return Task.CompletedTask;
        }
        return Hosting.HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () => ExecuteCurrentResponseAsync(action,
                CaptureCurrentResponseInput(CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision),
                    Interlocked.Read(ref workspaceSurfaceInputRevision)), SecurityAuditInitiator.LocalUser));
    }

    private async Task ExecuteCurrentResponseCommandAsync(CurrentResponseCommand command, string original,
        SecurityAuditInitiator initiator, ManualCallInput? manualInput, CurrentResponseInput? captured)
    {
        var origin = OriginalOrigin(initiator);
        if (command.Action is not { } action || !isAssistantNameAvailable
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || (origin == RequestOrigin.ActivatedVoice) != (initiator == SecurityAuditInitiator.VoiceCommand)
            || origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(original, AssistantName))
        {
            Transcript = "Current response command was not accepted. " + CurrentResponseCommand.Syntax;
            return;
        }
        if (action == CurrentResponseAction.Help)
        {
            Transcript = CurrentResponseCommand.Syntax + " Only the exact visible ordinary response is eligible. Questions, approvals, previews and private/retired sources are refused. No pin, preferences, model, speech or copy action.";
            return;
        }
        var input = captured ?? CaptureCurrentResponseInput(
            manualInput ?? CaptureManualCallInput(origin, CallPolicyRevision), Interlocked.Read(ref workspaceSurfaceInputRevision));
        await Hosting.HostRequestRunner.RunAsync(origin, () => ExecuteCurrentResponseAsync(action, input, initiator));
    }

    private async Task ExecuteCurrentResponseAsync(CurrentResponseAction action, CurrentResponseInput input,
        SecurityAuditInitiator initiator)
    {
        const string unavailable = "Current response action unavailable: the exact ordinary source, input, privacy, ownership or native acknowledgement is no longer valid. Nothing was replaced or approved.";
        Transcript = unavailable;
        var presenter = currentResponsePresentation;
        var binding = Interlocked.Read(ref currentResponseBindingRevision);
        var source = input.Source;
        bool HostAdmitted() => input.Eligible() && ReferenceEquals(presenter, currentResponsePresentation)
            && binding == Interlocked.Read(ref currentResponseBindingRevision);
        bool Admitted() => HostAdmitted() && source is not null && ReferenceEquals(source, Volatile.Read(ref ordinaryResponse));
        if (presenter is null || !Admitted()) { return; }
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            action == CurrentResponseAction.Dismiss ? "response.dismiss" : "response.details",
            SecurityAuditOutcome.Requested, initiator, source!.Id.ToString("D"));
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation);
        var requested = false;
        try
        {
            var lifetime = presenter.CaptureLifetime();
            await WriteAuditAsync(audit);
            requested = true;
            var confirmed = false;
            await uiDispatcher.InvokeAsync(() =>
            {
                using var activity = continuation();
                activity.Complete(HostOperationOutcome.Failed);
                if (Admitted() && lifetime())
                {
                    if (action == CurrentResponseAction.Dismiss)
                    {
                        confirmed = presenter.Dismiss(Admitted) && HostAdmitted()
                            && Volatile.Read(ref ordinaryResponse) is null
                            && Interlocked.Read(ref ordinaryResponseRevision) == source.Revision + 1;
                        if (confirmed) { State = AssistantState.Hidden; }
                    }
                    else
                    {
                        var request = source.Request ?? throw new InvalidOperationException("Original host response identity unavailable.");
                        var content = new AdmittedDetailContent(new(new(source.Id), source.Revision),
                            source.ModelAnswer is null ? DetailContentKind.PlainText : DetailContentKind.Markdown,
                            DetailContentOrigin.FinalizedResponse,
                            DetailSensitivity.DisclosureConfirmationRequired, "Current response",
                            source.ModelAnswer is null ? "Exact ordinary response" : "Exact model answer. Generated locally; verify important details.",
                            source.ModelAnswer?.Body ?? source.Title + Environment.NewLine + source.Body,
                            new(request.SessionId, request.RequestId, request.TaskId));
                        confirmed = presenter.OpenDetails(content, Admitted) && Admitted() && lifetime();
                    }
                }
                activity.Complete(confirmed ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
                return Task.CompletedTask;
            });
            await WriteAuditAsync(audit.WithOutcome(confirmed ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Cancelled));
            if (confirmed && HostAdmitted())
            { Transcript = action == CurrentResponseAction.Dismiss ? "Current response dismissed." : Admitted() && lifetime()
                ? "Exact current response details opened." : unavailable; }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            CurrentResponseActionLog.CurrentResponseActionFailed(logger, exception.GetType().Name);
            if (requested)
            {
                try { await WriteAuditAsync(audit.WithOutcome(SecurityAuditOutcome.Failed)); }
                catch (Exception auditException) when (auditException is InvalidOperationException or IOException
                    or UnauthorizedAccessException or OperationCanceledException)
                { CurrentResponseActionLog.CurrentResponseActionFailed(logger, auditException.GetType().Name); }
            }
        }
    }
}
