using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private readonly Kora.Application.IUiDispatcher conversationDispatcher =
        conversationPresentationDispatcher ?? new AvaloniaUiDispatcher();
    private ConversationExecutionStatus? runningConversation;
    private bool observingConversationCancellation;
    private bool cancellingConversation;
    private string runningConversationStatus = "No running conversation observed. Refresh running work to inspect this exact session.";
    private ConversationAnswerPresentation? provisionalAnswer;
    private long provisionalEpoch;
    private long provisionalControl;
    public string ProvisionalAnswer => provisionalAnswer is { } answer && !closed && access.CanInspect
        && access.ControlRevision == provisionalControl && selectionEpoch == provisionalEpoch
        && answer.Target.Request.SessionId == selected?.Authority.SessionId
        && answer.Target.Generation == selected.Authority.Generation && answer.IsCurrent()
        ? answer.Update.Answer : string.Empty;
    public bool HasProvisionalAnswer => ProvisionalAnswer.Length != 0;

    public string RunningConversationStatus => closed || !access.CanInspect ? "Private running-work presentation cleared."
        : runningConversation is { Phase: ConversationExecutionPhase.Waiting or ConversationExecutionPhase.Running } observed
            && conversation?.IsExecutionProgressCurrent(observed) != true
            ? "The observed execution step is no longer current. Refresh this exact conversation; owned work may still be running."
            : runningConversationStatus;
    public bool CanRefreshRunningConversation => !closed && access.CanInspect && selected is { Authority.IsActive: true }
        && conversation is not null && !cancellingConversation;
    public bool CanCancelRunningConversation => CanRefreshRunningConversation && access.CanControl
        && runningConversation is { Phase: ConversationExecutionPhase.Running or ConversationExecutionPhase.Waiting } current
        && current.Target.Request.SessionId == selected?.Authority.SessionId
        && current.Target.Generation == selected.Authority.Generation
        && conversation!.IsExecutionProgressCurrent(current);

    private void ObserveConversationCancellation()
    {
        if (closed || conversation is null || observingConversationCancellation) { return; }
        observingConversationCancellation = true;
        conversation.RunningConversationChanged += PresentConversationExecution;
        conversation.ProvisionalAnswerChanged += PresentProvisionalAnswerAsync;
        conversation.PendingConversationChanged += PresentPendingConversation;
        conversation.SubmittedConversationCompleted += PresentSubmittedConversation;
    }

    private async ValueTask PresentProvisionalAnswerAsync(ConversationAnswerPresentation answer, CancellationToken token)
    {
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (closed || !access.CanInspect || access.ControlRevision != revision || selectionEpoch != epoch
                || answer.Target.Request.SessionId != selected?.Authority.SessionId
                || answer.Target.Generation != selected.Authority.Generation || !answer.IsCurrent()) { return; }
            provisionalAnswer = answer;
            provisionalEpoch = epoch;
            provisionalControl = revision;
            OnPropertyChanged(nameof(ProvisionalAnswer));
            OnPropertyChanged(nameof(HasProvisionalAnswer));
        });
    }

    private void PresentConversationExecution(ConversationExecutionStatus observation)
    {
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        var target = selected;
        void Present()
        {
            if (closed || !access.CanInspect || access.ControlRevision != revision
                || selectionEpoch != epoch || selected != target
                || observation.Target.Request.SessionId != selected?.Authority.SessionId
                || observation.Target.Generation != selected.Authority.Generation) { return; }
            if (observation.Phase is ConversationExecutionPhase.Waiting or ConversationExecutionPhase.Running
                && conversation?.IsExecutionProgressCurrent(observation) != true)
            {
                // Retirement also uses this notification to invalidate already-bound provisional output.
                NotifyConversationCancellation();
                return;
            }
            if (runningConversation is { } current && current.Target.LeaseId == observation.Target.LeaseId
                && current.Target.Request == observation.Target.Request
                && current.Phase > observation.Phase) { return; }
            runningConversation = observation;
            if (observation.Phase != ConversationExecutionPhase.Running) { provisionalAnswer = null; }
            runningConversationStatus = "Task " + observation.Target.Request.TaskId.Value.ToString("D") + ": " + (observation.Phase switch
            {
                ConversationExecutionPhase.Waiting => "Waiting for admitted runtime resources. Cancel applies only to this displayed request.",
                ConversationExecutionPhase.Running => observation.Progress is { } progress
                    ? (progress.Step == ConversationExecutionStep.ReadingObservation
                        ? "Running admitted observation " + progress.ObservationCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". "
                        : "Running admitted model answer generation. ")
                        + "Host execution status only, not agent reasoning. Cancel applies only to this displayed task."
                    : "Running admitted conversation request. Cancel applies only to this displayed task.",
                ConversationExecutionPhase.Cancelling => "Cancelling requested; waiting for owned resources to terminate. No completed cancellation or rollback is claimed.",
                ConversationExecutionPhase.Completing => "Terminal storage owns the outcome. Cancellation cannot roll back a committed answer.",
                ConversationExecutionPhase.Terminal => "Terminal " + observation.Outcome + "; durable task " + observation.TaskState
                    + ". Owned resources joined and exact retained outcome confirmed. Dispatched work is never claimed rolled back.",
                _ => "Terminal outcome unconfirmed. Inspect this exact task; no replay, rollback or successful cancellation inferred.",
            }) + (observation.Resources is { } resources
                ? $" Last host resource check: configured maximum {resources.ConfiguredMaximum}; captured maximum {resources.CapturedMaximum}; "
                    + $"qualified ceiling {resources.QualifiedCeiling}; effective ceiling {resources.EffectiveCeiling}; "
                    + $"owned leases {resources.OwnedLeases}; waiting requests {resources.WaitingRequests}; blocker {resources.WaitReason}. "
                    + "A ceiling is not a promise of available resources."
                : string.Empty);
            NotifyConversationCancellation();
        }
        if (Dispatcher.UIThread.CheckAccess()) { Present(); }
        else { conversationDispatcher.Post(Present); }
    }

    public async Task RefreshRunningConversationAsync()
    {
        if (!CanRefreshRunningConversation) { return; }
        var target = RequireSelected();
        var epoch = selectionEpoch;
        bool Live() => !closed && access.CanInspect && selectionEpoch == epoch && selected == target;
        try
        {
            var observed = await conversation!.ReadRunningConversationAsync(target, Live, lifetime.Token);
            if (!Live()) { throw new OperationCanceledException("Original running-work selection changed."); }
            if (observed is not null) { PresentConversationExecution(observed); }
            else
            {
                runningConversation = null;
                pendingConversation = null;
                pendingConversationTurns = [];
                selectedPendingConversation = null;
                provisionalAnswer = null;
                runningConversationStatus = "No active admitted conversation lease in this exact session. Retained task receipts remain authoritative.";
                NotifyConversationCancellation();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            runningConversation = null;
            runningConversationStatus = "Running-work inspection unconfirmed: " + exception.Message;
            NotifyConversationCancellation();
        }
    }

    public async Task CancelRunningConversationAsync()
    {
        if (!CanCancelRunningConversation || runningConversation is not { } observed) { return; }
        var target = RequireSelected();
        var epoch = selectionEpoch;
        bool Live() => !closed && access.CanControl && selectionEpoch == epoch && selected == target;
        cancellingConversation = true;
        NotifyConversationCancellation();
        try
        {
            var accepted = await conversation!.CancelRunningConversationAsync(observed.Target, Live, lifetime.Token);
            if (Live()) { PresentConversationExecution(accepted); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (Live() && runningConversation?.Phase is not ConversationExecutionPhase.Terminal)
            {
                runningConversation = null;
                runningConversationStatus = "Cancellation acceptance unconfirmed: " + exception.Message
                    + " Inspect the exact task. No completed cancellation, rollback or retry inferred.";
            }
        }
        finally
        {
            cancellingConversation = false;
            NotifyConversationCancellation();
        }
    }

    private void ClearConversationCancellation()
    {
        runningConversation = null;
        runningConversationStatus = "No running conversation observed. Refresh running work to inspect this exact session.";
        if (closed && observingConversationCancellation && conversation is not null)
        {
            conversation.RunningConversationChanged -= PresentConversationExecution;
            conversation.ProvisionalAnswerChanged -= PresentProvisionalAnswerAsync;
            conversation.PendingConversationChanged -= PresentPendingConversation;
            conversation.SubmittedConversationCompleted -= PresentSubmittedConversation;
            observingConversationCancellation = false;
        }
        NotifyConversationCancellation();
    }

    private void NotifyConversationCancellation()
    {
        OnPropertyChanged(nameof(RunningConversationStatus));
        OnPropertyChanged(nameof(CanRefreshRunningConversation));
        OnPropertyChanged(nameof(CanCancelRunningConversation));
        OnPropertyChanged(nameof(ProvisionalAnswer));
        OnPropertyChanged(nameof(HasProvisionalAnswer));
        NotifyPendingConversation();
    }
}