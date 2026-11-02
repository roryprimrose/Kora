using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ConversationMessage? selectedAnswerRecord;
    private CancellationTokenSource? answerReadCancellation;
    private string answerReadStatus = "Select one retained ASSISTANT answer, then deliberately request one reading. Selection never starts speech.";
    private long answerSelectionRevision;

    public ConversationMessage? SelectedAnswerRecord
    {
        get => selectedAnswerRecord;
        set
        {
            if (ReferenceEquals(selectedAnswerRecord, value)) { return; }
            ClearFailedRetry();
            ClearExpiredOrdinary();
            CancelAnswerRead();
            selectedAnswerRecord = value is not null && ConversationMessages.Any(item => ReferenceEquals(item, value))
                ? value : null;
            Interlocked.Increment(ref answerSelectionRevision);
            NotifyAnswerRead();
        }
    }

    public bool CanReadSelectedAnswer => !closed && access.CanControl && CanReadConversation
        && answerReads is not null && answerReadCancellation is null
        && selectedAnswerRecord?.AssistantResponse is not null;
    public bool CanCancelAnswerRead => answerReadCancellation is not null;
    public string AnswerReadStatus => answerReadStatus;

    internal Func<CancellationToken, Task>? CaptureSelectedAnswerRead(Func<bool>? channelEligible = null)
    {
        if (!CanReadSelectedAnswer || selected is not { } target || selectedAnswerRecord is not { } source) { return null; }
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        var answerRevision = Interlocked.Read(ref answerSelectionRevision);
        var used = 0;
        bool Live() => !closed && access.CanControl && access.CanInspect && selected == target
            && access.ControlRevision == control && epoch == selectionEpoch
            && answerRevision == Interlocked.Read(ref answerSelectionRevision)
            && channelEligible?.Invoke() != false && ReferenceEquals(selectedAnswerRecord, source)
            && ConversationMessages.Any(item => ReferenceEquals(item, source));
        return async token =>
        {
            if (Interlocked.Exchange(ref used, 1) != 0 || !Live() || answerReadCancellation is not null
                || HostActivity.Current is null)
            {
                answerReadStatus = "Selected answer read refused: the exact selection or original request is stale. Nothing was retargeted or queued.";
                NotifyAnswerRead();
                return;
            }
            var original = HostActivity.RequireCurrent().Request;
            ActivityLink[] links = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
            using var activity = HostActivity.BeginRoot(new(original.RequestId, target.Authority.SessionId,
                original.TaskId, original.Origin, original.InvocationId), HostActivityLayer.Desktop, HostOperation.Request, links);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            answerReadCancellation = cancellation;
            NotifyAnswerRead();
            try
            {
                var result = await answerReads!.ReadOnceAsync(target, source,
                    () => Live() && !cancellation.IsCancellationRequested, cancellation.Token);
                if (!Live() || cancellation.IsCancellationRequested)
                { throw new OperationCanceledException("The exact retained answer presentation was retired."); }
                answerReadStatus = result;
                activity.Complete(HostOperationOutcome.Completed);
            }
            catch (OperationCanceledException)
            {
                answerReadStatus = "Selected answer reading cancelled. No successful full reading or replay is claimed.";
                activity.Complete(HostOperationOutcome.Cancelled);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                or AggregateException)
            {
                Failure(logger, exception.GetType().Name);
                answerReadStatus = "Selected answer reading unavailable or unconfirmed. Refresh the exact source and inspect speech readiness; no successful reading or replay is claimed.";
                activity.Complete(HostOperationOutcome.Failed);
            }
            finally
            {
                answerReadCancellation = null;
                NotifyAnswerRead();
            }
        };
    }

    public async Task ReadSelectedAnswerAsync()
    {
        if (CaptureSelectedAnswerRead() is not { } read) { await ReportAnswerReadUnavailable(); return; }
        if (HostActivity.HasScope)
        {
            await read(CancellationToken.None);
            return;
        }
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request);
        await read(CancellationToken.None);
        activity.Complete(HostOperationOutcome.Completed);
    }

    private Task ReportAnswerReadUnavailable()
    {
        answerReadStatus = "Select one exact retained ASSISTANT answer in this session. No USER text, current-response fallback or queued replay is permitted.";
        NotifyAnswerRead();
        return Task.CompletedTask;
    }

    public void CancelAnswerRead()
    {
        answerReadCancellation?.Cancel();
        Interlocked.Increment(ref answerSelectionRevision);
    }

    private void ClearAnswerRead()
    {
        CancelAnswerRead();
        selectedAnswerRecord = null;
        NotifyAnswerRead();
    }

    private void NotifyAnswerRead()
    {
        NotifyFailedRetry();
        if (selectedAnswerRecord is not null
            && !ConversationMessages.Any(item => ReferenceEquals(item, selectedAnswerRecord)))
        {
            CancelAnswerRead();
            selectedAnswerRecord = null;
        }
        OnPropertyChanged(nameof(SelectedAnswerRecord));
        OnPropertyChanged(nameof(CanReadSelectedAnswer));
        OnPropertyChanged(nameof(CanCancelAnswerRead));
        OnPropertyChanged(nameof(AnswerReadStatus));
    }
}
