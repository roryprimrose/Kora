using System.Globalization;

using Kora.Application.Presentation;
using Kora.Core.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private sealed record NoticeResultOrigin(DetailContentReference Reference, Func<bool> Current, Func<bool> Return);
    private NoticeResultOrigin? noticeResultOrigin;
    internal bool IsNoticeResult(AdmittedDetailContent? content) =>
        content is not null && noticeResultOrigin?.Reference == content.Reference;
    internal bool CanReturnToNoticeResult(AdmittedDetailContent? content) =>
        IsNoticeResult(content) && noticeResultOrigin!.Current();
    internal bool ReturnToNoticeResult(AdmittedDetailContent? content) =>
        CanReturnToNoticeResult(content) && noticeResultOrigin!.Return();

    public bool CanReadExactNoticeResult => CanControlLocalEvent && routineSurfaceOpen
        && openConversationDetail is not null && selectedEvent?.Event.Source == LocalEventSource.ConversationTask;

    public Task ReadExactNoticeResultAsync(Func<bool> returnToOrigin)
    {
        var originalAllowed = !HostActivity.HasScope || HostActivity.Current is { } original
            && original.Request.Origin == RequestOrigin.LocalUi && original.Request.InvocationId is null
            && !original.Activity!.IsStopped;
        var session = selected?.Authority.SessionId;
        return RunAsync(async () =>
        {
            if (!originalAllowed)
            { throw new InvalidOperationException("Read exact result requires deliberate native user input, not relabelled model/system/voice authority."); }
            var target = RequireSelected();
            var notice = SelectedLocalEvent ?? throw new InvalidOperationException("Select one exact current terminal conversation notice.");
            var broker = localEvents ?? throw new InvalidOperationException("The local event broker is unavailable.");
            var open = openConversationDetail ?? throw new InvalidOperationException("The integrated Details reader is unavailable.");
            var epoch = selectionEpoch;
            var filterEpoch = eventFilterEpoch;
            var choiceEpoch = eventChoiceEpoch;
            var control = access.ControlRevision;
            var surface = Volatile.Read(ref routineSurfaceRevision);
            bool Current() => !closed && routineSurfaceOpen && Volatile.Read(ref routineSurfaceRevision) == surface
                && access.CanInspect && access.CanControl && access.ControlRevision == control
                && selectionEpoch == epoch && eventFilterEpoch == filterEpoch && eventChoiceEpoch == choiceEpoch
                && EventSourceCurrent && selected == target && selectedEvent == notice && LocalEvents.Contains(notice);
            if (!Current() || notice.Event.Source != LocalEventSource.ConversationTask)
            { throw new InvalidOperationException("Read exact result requires the visible terminal notice in its already-open Sessions origin."); }

            async Task Validate(CancellationToken token)
            {
                var reviewed = await broker.ExecuteNativeAsync(
                    new(LocalEventOperation.Review, notice.Event.Id, notice.Event.Revision), Current, token);
                if (!Current() || reviewed.Event != notice.Event || reviewed.Reason is LocalEventReason.SourceUnavailable
                    or LocalEventReason.AdmissionChanged or LocalEventReason.Expired or LocalEventReason.ClockRollback)
                { throw new InvalidOperationException("Unavailable: the original terminal notice expired or changed. Inspect ordinary history separately; no notice was revived."); }
            }
            async Task<AdmittedDetailContent> Read(CancellationToken token)
            {
                await Validate(token);
                var resolved = await service.ReadConversationNoticeResultAsync(target, notice.Event, Current, token);
                await Validate(token);
                return resolved;
            }
            var content = await Read(lifetime.Token);
            noticeResultOrigin = new(content.Reference, Current, returnToOrigin);
            var beginValidation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Storage);
            async Task<AdmittedDetailContent> Resolve(CancellationToken token)
            {
                using var validation = beginValidation();
                try
                {
                    var exact = await Read(token);
                    validation.Complete(HostOperationOutcome.Completed);
                    return exact;
                }
                catch (OperationCanceledException) { validation.Complete(HostOperationOutcome.Cancelled); throw; }
                catch { validation.Complete(HostOperationOutcome.Failed); throw; }
            }
            var result = await open(content, Current, Resolve, lifetime.Token);
            var final = await Read(lifetime.Token);
            if (!Current() || !content.IsSameSnapshot(final))
            { throw new OperationCanceledException("The exact notice reader origin was retired; no late result was published."); }
            status = result;
        }, activitySession: session);
    }

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
