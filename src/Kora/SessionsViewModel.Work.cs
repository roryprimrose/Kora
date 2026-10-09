using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionWorkSnapshot? workSnapshot;
    private IReadOnlyList<SessionWorkRow> workRecords = [];
    private SessionWorkRow? selectedWork;
    private long selectionEpoch;
    private bool presentingWork;
    public IReadOnlyList<SessionWorkRow> WorkRecords => workRecords;
    public IReadOnlyList<SessionWorkQuestion> PendingQuestions => workSnapshot?.PendingQuestions ?? [];
    public SessionWorkRow? SelectedWorkRecord => selectedWork;
    public string WorkStatus => workSnapshot is { } snapshot
        ? "Exact session " + snapshot.Session.Authority.SessionId.Value.ToString("D")
            + " | generation " + snapshot.Session.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | authority revision " + snapshot.AuthorityRevision.ToString(CultureInfo.InvariantCulture)
            + " | queue revision " + snapshot.Queue.Revision.ToString(CultureInfo.InvariantCulture)
            + "\nObserved " + snapshot.ObservedAt.ToString("O", CultureInfo.InvariantCulture)
            + " | pending capacity " + snapshot.PendingCount.ToString(CultureInfo.InvariantCulture) + "/"
            + snapshot.PendingCapacity.ToString(CultureInfo.InvariantCulture)
            + " (expired entries still occupy capacity) | global slots " + snapshot.ExecutionSlots.ToString(CultureInfo.InvariantCulture)
            + "\nGaps: " + snapshot.OmittedTasks.ToString(CultureInfo.InvariantCulture) + " other task records, "
            + snapshot.OmittedQuestions.ToString(CultureInfo.InvariantCulture)
            + " pending questions, " + snapshot.OmittedQueueRecords.ToString(CultureInfo.InvariantCulture)
            + " older queue receipts omitted; recent queue receipts limited to "
            + SessionWorkSnapshot.RecentQueueRecords.ToString(CultureInfo.InvariantCulture)
            + ". Use exact history/task status for other retained records. Eligibility is observed, never permission."
        : "Live work unavailable/not yet read; never interpreted as an empty queue. Refresh selected work.";
    public bool CanRefreshWork => CanRead && selected is not null;
    public bool CanInspectWork => CanRead && selectedWork is not null;
    public bool CanCancelWork => CanCancelQueueEntry || CanCancelTask;
    public bool CanRemoveQueueEntry => CanCancelQueueEntry;
    public bool CanClearQueue => CanControlQueue && queueSnapshot!.Entries.Any(entry => entry.State is SessionQueueState.Pending or SessionQueueState.Expired);
    public bool CanEnqueueVersion => CanControlQueue && workSnapshot is { } snapshot && snapshot.PendingCount < snapshot.PendingCapacity;

    public void SelectWorkRecord(SessionWorkRow? row)
    {
        if (closed || presentingWork || row is not null && !workRecords.Contains(row)) { return; }
        selectionEpoch++;
        selectedWork = row;
        selectedQueueEntry = row?.Queue?.Entry;
        selectedTask = row?.Task?.Task;
        inspectedTask = row?.Task;
        Notify();
    }

    public Task InspectWorkAsync() => selectedQueueEntry is not null
        ? RunQueueCommandAsync(SessionCommandOperation.QueueStatus) : InspectTaskAsync();
    public Task CancelWorkAsync() => selectedQueueEntry is not null ? CancelQueueEntryAsync() : CancelTaskAsync();
    public Task RemoveQueueEntryAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueRemove);
    public Task RefreshWorkAsync() => RunAsync(() => RefreshSelectedWorkAsync(concurrent: true), passive: true);

    private async Task RefreshSelectedWorkAsync(bool concurrent = false)
    {
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var snapshot = await service.ReadWorkAsync(target.Authority.SessionId, lifetime.Token);
        if (concurrent && (busy || selectionEpoch != epoch || selected?.Authority.SessionId != target.Authority.SessionId)) { return; }
        if (selectionEpoch != epoch || selected?.Authority.SessionId != target.Authority.SessionId)
        {
            throw new OperationCanceledException("The selected session changed during observation.");
        }
        ApplyWork(snapshot);
        await RefreshLocalEventsAsync(target.Authority.SessionId, epoch);
    }

    private void ApplyWork(SessionWorkSnapshot? snapshot)
    {
        if (snapshot is null) { throw new InvalidOperationException("The authoritative work snapshot service is unavailable."); }
        var selectedId = selectedWork?.TaskId;
        workSnapshot = snapshot;
        selected = snapshot.Session;
        queueSnapshot = snapshot.Queue;
        workRecords = [.. snapshot.QueueRecords.Select(record => new SessionWorkRow(record, null, snapshot.ObservedAt)),
            .. snapshot.Tasks.Select(task => new SessionWorkRow(null, task, snapshot.ObservedAt))];
        selectedWork = workRecords.FirstOrDefault(row => row.TaskId == selectedId);
        selectedQueueEntry = selectedWork?.Queue?.Entry;
        selectedTask = selectedWork?.Task?.Task;
        // New observations never silently renew an inspected cancellation target.
        inspectedTask = null;
        NotifyWork();
    }

    private void NotifyWork()
    {
        presentingWork = true;
        try
        {
            OnPropertyChanged(nameof(WorkRecords));
            OnPropertyChanged(nameof(SelectedWorkRecord));
            OnPropertyChanged(nameof(PendingQuestions));
            OnPropertyChanged(nameof(WorkStatus));
            OnPropertyChanged(nameof(CanRefreshWork));
            OnPropertyChanged(nameof(CanInspectWork));
            OnPropertyChanged(nameof(CanCancelWork));
            OnPropertyChanged(nameof(CanRemoveQueueEntry));
            OnPropertyChanged(nameof(CanClearQueue));
            OnPropertyChanged(nameof(CanEnqueueVersion));
        }
        finally { presentingWork = false; }
    }
}