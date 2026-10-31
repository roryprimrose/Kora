using System.Globalization;

using Kora.Core.Interaction;
using Kora.Application.Presentation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private LocalEvent? reviewedConversationNotice;
    private bool changingConversationNotice;
    internal Func<LocalEvent, IPendingQuestionFocus?>? ConversationNoticeQuestion { get; set; }
    public bool CanAnswerConversationNotice => CanControlLocalEvent && routineSurfaceOpen
        && ConversationNoticeQuestion is not null && SelectedLocalEvent?.Event == reviewedConversationNotice
        && reviewedConversationNotice?.Source == LocalEventSource.ConversationQuestion;

    public Task AnswerConversationNoticeAsync()
    {
        var target = CanAnswerConversationNotice ? reviewedConversationNotice : null;
        var capture = ConversationNoticeQuestion;
        var original = !HostActivity.HasScope || HostActivity.Current is { Outcome: HostOperationOutcome.Unknown } input
            && input.Request.Origin == RequestOrigin.LocalUi && input.Request.InvocationId is null && !input.Activity!.IsStopped;
        return RunConversationNoticeAsync(async () =>
        {
            var focus = target is null ? null : capture?.Invoke(target);
            if (!original || target is null || focus is null || localEvents is null)
            { throw new InvalidOperationException("Review one exact current conversation question notice first. Its original native question must still be available; no replacement was opened."); }
            var epoch = selectionEpoch;
            var filters = eventFilterEpoch;
            var surface = Volatile.Read(ref routineSurfaceRevision);
            bool Eligible() => !closed && routineSurfaceOpen && surface == Volatile.Read(ref routineSurfaceRevision)
                && epoch == selectionEpoch && filters == eventFilterEpoch && EventSourceCurrent
                && selectedEvent?.Event == target && reviewedConversationNotice == target && localEvents.IsHeld(target);
            var result = await localEvents.ExecuteNativeAsync(new(LocalEventOperation.Review, target.Id, target.Revision),
                Eligible, lifetime.Token);
            if (!Eligible() || result.Reason is LocalEventReason.SourceUnavailable or LocalEventReason.AdmissionChanged
                or LocalEventReason.Expired or LocalEventReason.ClockRollback || !await focus.FocusAsync(Eligible) || !Eligible())
            { throw new InvalidOperationException("The exact notice/question, original input or native presenter changed or is busy. No other question was opened; refresh and review separately."); }
            reviewedConversationNotice = null;
            status = "Exact original conversation question opened. Answer or Cancel there using its existing controls. Opening is not an answer, approval, microphone or transmission grant.";
        });
    }

    private async Task RunConversationNoticeAsync(Func<Task> operation)
    {
        if (closed || changingConversationNotice || busy && !conversationBusy) { return; }
        var original = !HostActivity.HasScope || HostActivity.Current is { Outcome: HostOperationOutcome.Unknown } input
            && input.Request.Origin == RequestOrigin.LocalUi && input.Request.InvocationId is null && !input.Activity!.IsStopped;
        changingConversationNotice = true;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()),
            selected?.Authority.SessionId ?? viewer.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Presentation);
        NotifyLocalEvents();
        try
        {
            if (!original || !access.CanInspect || !access.CanControl)
            { throw new InvalidOperationException("Private notice control is unavailable."); }
            await operation();
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ClearLocalEvents();
            status = "Conversation notice unavailable: " + exception.Message
                + ". Refresh and review the exact current notice. No question was replaced, answered or resumed.";
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            changingConversationNotice = false;
            NotifyLocalEvents();
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(Detail));
            if (closed && !busy && !protectingComposer && !refreshingWork && !changingRoutineQuiet && !changingQueueControl) { lifetime.Dispose(); }
        }
    }

    private static string ConversationNoticeDetail(LocalEventView notice) =>
        notice.Event.Summary + "\nExact task: " + notice.Event.TaskId!.Value.Value.ToString("D")
        + "\nExact session: " + notice.Event.SessionId.Value.ToString("D")
        + (notice.Event.Source == LocalEventSource.ConversationQuestion
            ? "\nExact question: " + notice.Event.SubjectId.ToString("D") : string.Empty)
        + "\nObserved status: " + notice.Event.Type + "; delivery: " + notice.Reason
        + "\nOriginal expiry: " + notice.Event.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        + "\nInspection only. The selected conversation, work selection and voice target are unchanged. "
        + "Read the exact task in Work for its retained status; use Answer exact question only after this review to open the existing canonical Questions window. "
        + "No answer, approval, dispatch, retry, speech or deadline extension is authorised by a notice.";
}
