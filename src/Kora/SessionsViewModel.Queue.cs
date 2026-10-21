using System.Text;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionQueueSnapshot? queueSnapshot;
    private SessionQueueEntry? selectedQueueEntry;
    public IReadOnlyList<SessionQueueEntry> QueueRecords => queueSnapshot?.Entries ?? [];
    public bool CanReadQueue => CanRead && selected is not null;
    public bool CanControlQueue => CanReadQueue && access.CanControl && selected?.Authority.IsActive == true
        && queueSnapshot?.SessionId == selected.Authority.SessionId;
    public bool CanCancelQueueEntry => CanControlQueue && selectedQueueEntry?.State is SessionQueueState.Pending or SessionQueueState.Expired;
    private bool changingQueueControl;

    public void SelectQueueEntry(SessionQueueEntry? entry)
    {
        if (!CanRead || entry is not null && !QueueRecords.Contains(entry)) { return; }
        selectedQueueEntry = entry;
        selectionEpoch++;
        NotifyQueue();
    }

    public Task ReadQueueAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueList);
    public Task ClearQueueAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueClear);
    public Task CancelQueueEntryAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueCancel);

    private Task RunQueueCommandAsync(SessionCommandOperation operation)
    {
        var epoch = selectionEpoch;
        var selectedId = selected?.Authority.SessionId;
        var entry = selectedQueueEntry;
        var snapshot = queueSnapshot;
        var observedAdmission = access.ControlRevision;
        return RunAsync(async () =>
        {
            var target = RequireSelected();
            if (operation != SessionCommandOperation.QueueList && operation != SessionCommandOperation.QueueStatus
                && snapshot?.SessionId != target.Authority.SessionId)
            {
                throw new InvalidOperationException("Read the exact selected queue and displayed revisions first.");
            }
            var command = new SessionCommand(operation, target.Authority.SessionId.Value, target.Authority.Generation.Value)
            {
                QueueRevision = snapshot?.Revision ?? 0,
                QueueRunId = snapshot?.DispatchControl?.RunId,
                QueueControlRevision = snapshot?.DispatchControl?.Revision ?? 0,
                TaskId = entry?.Request.TaskId.Value,
                TaskRevision = entry?.Revision.Value ?? 0,
            };
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanInspect && selectionEpoch == epoch && selected?.Authority.SessionId == selectedId,
                lifetime.Token);
            if (result.Work is { } work) { ApplyWork(work, observedAdmission); }
            else if (operation != SessionCommandOperation.QueueStatus)
            {
                queueSnapshot = result.Queue;
                selectedQueueEntry = null;
                await RefreshSelectedWorkAsync();
            }
            Detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(result));
            status = "Exact fixed-read queue " + result.Outcome
                + ". Existing records only; no new diagnostic admission, dispatch or replay.";
            NotifyQueue();
        });
    }

    private void NotifyQueue()
    {
        OnPropertyChanged(nameof(QueueRecords));
        OnPropertyChanged(nameof(CanReadQueue));
        OnPropertyChanged(nameof(CanControlQueue));
        OnPropertyChanged(nameof(CanCancelQueueEntry));
        NotifyPendingCancellation();
    }
}