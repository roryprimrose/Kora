using System.Diagnostics;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal Func<bool> CaptureQueueCancellationInputEligibility(bool requireIdle = true)
    {
        var body = composerBody;
        var queue = queueSnapshot;
        var subset = pendingCancellationEpoch;
        var selection = selectionEpoch;
        var draft = savedConversationDraft;
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var review = queueCancelReview;
        var page = sessions;
        return () => !closed && !protectingComposer && !busy && !refreshingWork && !conversationBusy
            && (!requireIdle || !changingQueueControl) && selectionEpoch == selection
            && pendingCancellationEpoch == subset && ReferenceEquals(queueSnapshot, queue)
            && ReferenceEquals(queueCancelReview, review) && ReferenceEquals(sessions, page)
            && savedConversationDraft == draft
            && Interlocked.Read(ref composerBufferRevision) == buffer
            && string.Equals(composerBody, body, StringComparison.Ordinal);
    }

    internal async Task<bool> PrepareQueueCancellationAsync(SessionQueueCancellationPreparation preparation,
        Func<bool> admitted, CancellationToken token)
    {
        if (!CanRead || changingQueueControl || refreshingWork || conversationBusy)
        { throw new InvalidOperationException("Finish the original native operation before preparing cancellation."); }
        var prepared = false;
        var originalSelection = selectionEpoch;
        var originalQueue = queueSnapshot;
        var originalSubset = pendingCancellationEpoch;
        bool Original() => admitted() && !closed && access.ControlRevision == preparation.ControlRevision
            && selectionEpoch == originalSelection && ReferenceEquals(queueSnapshot, originalQueue)
            && pendingCancellationEpoch == originalSubset;
        ActivityLink[] links = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        await RunAsync(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            if (!Original()) { throw new OperationCanceledException("Original cancellation source changed."); }
            bool protectedComposer;
            try { protectedComposer = await ProtectComposerAsync(Original); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ReportComposerCloseFailure(exception);
                status = "Queue cancellation review unavailable: original input admission changed. Unsent messages remain held; inspect the exact source before retrying.";
                return;
            }
            if (!protectedComposer)
            {
                status = "Queue cancellation review not opened. Original composer decision cancelled; no pending work changed.";
                return;
            }
            var originalInput = CaptureQueueCancellationInputEligibility(requireIdle: false);
            var session = preparation.Work.Session;
            var current = await service.ReadWorkAsync(session.Authority.SessionId, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Original() || !originalInput() || current.Session != session || current.Queue.Revision != preparation.Work.Queue.Revision
                || current.Queue.DispatchControl != preparation.Work.Queue.DispatchControl
                || !current.Queue.Entries.SequenceEqual(preparation.Work.Queue.Entries))
            { throw new OperationCanceledException("The original exact session, queue, run, pause or tasks changed. No partial preparation."); }
            var entries = SessionQueueCancellation.Select(current.Queue, preparation.Selection);
            LeaveListSearch();
            sessionFilter = SessionListFilter.All;
            if (selected != session)
            {
                ClearSelection();
                selected = session;
                sessions = new([session], null);
                historySessionId = session.Authority.SessionId.Value.ToString("D");
                nameDraft = session.Metadata?.Name.Value ?? string.Empty;
            }
            ApplyWork(current, preparation.ControlRevision);
            OnPropertyChanged(nameof(SessionFilter));
            SetPendingCancellationEntries(entries.Select(entry => current.Queue.Entries.Single(
                original => original.Request.TaskId == entry.Request.TaskId)).ToArray());
            var selection = selectionEpoch;
            var body = composerBody;
            var buffer = Interlocked.Read(ref composerBufferRevision);
            queueCancellationAdmission = () => admitted() && !closed && selectionEpoch == selection
                && access.ControlRevision == preparation.ControlRevision
                && Interlocked.Read(ref composerBufferRevision) == buffer
                && string.Equals(composerBody, body, StringComparison.Ordinal);
            await ReviewPendingCancellationCoreAsync(cancellation.Token);
            prepared = true;
        }, activitySession: preparation.Work.Session.Authority.SessionId, dispatchControl: true, activityLinks: links);
        return prepared && CanConfirmPendingCancellation;
    }
}
