using Avalonia.Threading;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ConversationExecutionStatus? runningConversation;
    private bool observingConversationCancellation;
    private bool cancellingConversation;
    private string runningConversationStatus = "No running conversation observed. Refresh running work to inspect this exact session.";

    public string RunningConversationStatus => !closed && access.CanInspect ? runningConversationStatus : "Private running-work presentation cleared.";
    public bool CanRefreshRunningConversation => !closed && access.CanInspect && selected is { Authority.IsActive: true }
        && conversation is not null && !cancellingConversation;
    public bool CanCancelRunningConversation => CanRefreshRunningConversation && access.CanControl
        && runningConversation is { Phase: ConversationExecutionPhase.Running or ConversationExecutionPhase.Waiting } current
        && current.Target.Request.SessionId == selected?.Authority.SessionId
        && current.Target.Generation == selected.Authority.Generation;

    private void ObserveConversationCancellation()
    {
        if (closed || conversation is null || observingConversationCancellation) { return; }
        observingConversationCancellation = true;
        conversation.RunningConversationChanged += PresentConversationExecution;
    }

    private void PresentConversationExecution(ConversationExecutionStatus observation)
    {
        var revision = access.ControlRevision;
        void Present()
        {
            if (closed || !access.CanInspect || access.ControlRevision != revision
                || observation.Target.Request.SessionId != selected?.Authority.SessionId
                || observation.Target.Generation != selected.Authority.Generation) { return; }
            if (runningConversation is { } current && current.Target == observation.Target
                && current.Phase > observation.Phase) { return; }
            runningConversation = observation;
            runningConversationStatus = "Task " + observation.Target.Request.TaskId.Value.ToString("D") + ": " + (observation.Phase switch
            {
                ConversationExecutionPhase.Waiting => "Waiting for admitted runtime resources. Cancel applies only to this displayed request.",
                ConversationExecutionPhase.Running => "Running admitted conversation request. Cancel applies only to this displayed task.",
                ConversationExecutionPhase.Cancelling => "Cancelling requested; waiting for owned resources to terminate. No completed cancellation or rollback is claimed.",
                ConversationExecutionPhase.Completing => "Terminal storage owns the outcome. Cancellation cannot roll back a committed answer.",
                ConversationExecutionPhase.Terminal => "Terminal " + observation.Outcome + "; durable task " + observation.TaskState
                    + ". Owned resources joined and exact retained outcome confirmed. Dispatched work is never claimed rolled back.",
                _ => "Terminal outcome unconfirmed. Inspect this exact task; no replay, rollback or successful cancellation inferred.",
            });
            NotifyConversationCancellation();
        }
        if (Dispatcher.UIThread.CheckAccess()) { Present(); }
        else { Dispatcher.UIThread.Post(Present); }
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
            observingConversationCancellation = false;
        }
        NotifyConversationCancellation();
    }

    private void NotifyConversationCancellation()
    {
        OnPropertyChanged(nameof(RunningConversationStatus));
        OnPropertyChanged(nameof(CanRefreshRunningConversation));
        OnPropertyChanged(nameof(CanCancelRunningConversation));
    }
}