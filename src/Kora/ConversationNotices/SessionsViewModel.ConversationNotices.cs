using System.Globalization;

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

    private static string ConversationNoticeDetail(LocalEventView notice) =>
        notice.Event.Summary + "\nExact task: " + notice.Event.TaskId!.Value.Value.ToString("D")
        + "\nObserved status: " + notice.Event.Type + "; delivery: " + notice.Reason
        + "\nOriginal expiry: " + notice.Event.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        + "\nInspection only. The selected conversation, work selection and voice target are unchanged. "
        + "Read the exact task in Work for its retained status; answer any still-current question separately in Questions. "
        + "No answer, approval, dispatch, retry, speech or deadline extension is authorised by a notice.";
}
