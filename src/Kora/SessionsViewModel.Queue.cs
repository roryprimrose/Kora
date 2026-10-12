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
    private bool dispatchingQueue;
    public bool CanChangeQueuePause => (!busy || dispatchingQueue) && !closed && !changingQueueControl && access.CanControl
        && selected?.Authority.IsActive == true && queueSnapshot?.SessionId == selected.Authority.SessionId
        && queueSnapshot.Generation == selected.Authority.Generation && queueSnapshot.DispatchControl is not null;

    public void SelectQueueEntry(SessionQueueEntry? entry)
    {
        if (!CanRead || entry is not null && !QueueRecords.Contains(entry)) { return; }
        selectedQueueEntry = entry;
        queueRequeueReview = null;
        selectionEpoch++;
        NotifyQueue();
    }

    public Task ReadQueueAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueList);
    public Task EnqueueVersionAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueEnqueue);
    public Task EnqueueCapabilitiesAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueEnqueue, SessionQueueReadProfile.Capabilities);
    public Task EnqueueReadinessAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueEnqueue, SessionQueueReadProfile.Readiness);
    public Task EnqueueRuntimesAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueEnqueue, SessionQueueReadProfile.Runtimes);
    public Task DispatchQueueAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueDispatch);
    public Task ClearQueueAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueClear);
    public Task CancelQueueEntryAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueCancel);
    public Task PauseQueueAsync() => RunQueueCommandAsync(SessionCommandOperation.QueuePause);
    public Task ResumeQueueAsync() => RunQueueCommandAsync(SessionCommandOperation.QueueResume);

    private Task RunQueueCommandAsync(SessionCommandOperation operation, SessionQueueReadProfile profile = SessionQueueReadProfile.Version,
        SessionQueueCapabilityTarget? capabilityTarget = null)
    {
        var epoch = selectionEpoch;
        var selectedId = selected?.Authority.SessionId;
        var entry = selectedQueueEntry;
        var snapshot = queueSnapshot;
        var observedAdmission = access.ControlRevision;
        if (operation == SessionCommandOperation.QueueList) { InvalidateQueuePrerequisite(clearSource: true); }
        var prerequisite = queuePrerequisite;
        var prerequisiteEpoch = queuePrerequisiteEpoch;
        var capability = queueCapability;
        bool CapabilityCurrent() => profile != SessionQueueReadProfile.Capability
            || QueueCapabilityCurrent && ReferenceEquals(queueCapability, capability);
        var controlsDispatch = operation is SessionCommandOperation.QueuePause or SessionCommandOperation.QueueResume;
        return RunAsync(async () =>
        {
            var target = RequireSelected();
            if (operation == SessionCommandOperation.QueueEnqueue)
            {
                SessionQueueReadProfiles.Validate(profile, capabilityTarget);
                if (!CapabilityCurrent()) { throw new InvalidOperationException("The exact descriptor choice is stale. Refresh and explicitly reselect."); }
            }
            if (operation == SessionCommandOperation.QueueEnqueue && !QueuePrerequisiteStillCurrent(prerequisite, prerequisiteEpoch))
            {
                throw new InvalidOperationException("The prerequisite choice is stale or missing. Refresh work and explicitly choose None or reselect the exact task.");
            }
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
                WorkRequestId = operation == SessionCommandOperation.QueueEnqueue ? Guid.NewGuid() : null,
                TaskId = operation == SessionCommandOperation.QueueEnqueue ? Guid.NewGuid() : entry?.Request.TaskId.Value,
                TaskRevision = entry?.Revision.Value ?? 0,
                QueueProfile = profile,
                QueueTarget = capabilityTarget,
                DependencyTaskId = operation == SessionCommandOperation.QueueEnqueue ? prerequisite?.TaskId.Value : null,
                ExpectedDependencyRevision = operation == SessionCommandOperation.QueueEnqueue ? prerequisite?.Revision : null,
            };
            var result = await service.ExecuteCommandAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanInspect && selectionEpoch == epoch && selected?.Authority.SessionId == selectedId
                    && (operation != SessionCommandOperation.QueueEnqueue || QueuePrerequisiteStillCurrent(prerequisite, prerequisiteEpoch)
                        && CapabilityCurrent()), lifetime.Token);
            if (operation == SessionCommandOperation.QueueEnqueue) { InvalidateQueuePrerequisite(requireDecision: true); }
            if (result.Work is { } work) { ApplyWork(work, observedAdmission); }
            else if (operation != SessionCommandOperation.QueueStatus)
            {
                queueSnapshot = result.Queue;
                selectedQueueEntry = null;
                await RefreshSelectedWorkAsync();
            }
            detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(result));
            status = "Exact fixed-read queue " + result.Outcome
                + ". Dispatch is manual and fair among already queued sessions; no model, effects, audio or restart replay.";
            NotifyQueue();
        }, dispatchControl: controlsDispatch, queueDispatch: operation == SessionCommandOperation.QueueDispatch);
    }

    private void NotifyQueue()
    {
        OnPropertyChanged(nameof(QueueRecords));
        OnPropertyChanged(nameof(CanReadQueue));
        OnPropertyChanged(nameof(CanControlQueue));
        OnPropertyChanged(nameof(CanCancelQueueEntry));
        OnPropertyChanged(nameof(CanChangeQueuePause));
        NotifyQueueMove();
        NotifyPendingCancellation();
        NotifyExpiredRequeue();
        NotifyQueuePrerequisite();
    }
}