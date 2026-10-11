using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private IReadOnlyList<QueuePrerequisiteOption> queuePrerequisites = [];
    private QueuePrerequisiteOption? queuePrerequisite;
    private bool queuePrerequisiteDecisionRequired;
    private long queuePrerequisiteEpoch;
    private long queuePrerequisiteAdmission;
    private bool queuePrerequisiteMetadataAvailable;
    private bool presentingQueuePrerequisites;
    public IReadOnlyList<QueuePrerequisiteOption> QueuePrerequisites =>
        QueuePrerequisiteSourceCurrent ? queuePrerequisites : [];
    public QueuePrerequisiteOption? SelectedQueuePrerequisite => QueuePrerequisiteSourceCurrent ? queuePrerequisite : null;
    public bool CanSelectQueuePrerequisite => CanRead && CanControlQueue && QueuePrerequisiteSourceCurrent;
    private bool QueuePrerequisiteSourceCurrent => queuePrerequisiteMetadataAvailable && !closed && access.CanInspect && access.CanControl
        && queuePrerequisiteAdmission == access.ControlRevision && workSnapshot is { } work
        && work.Session.Authority == selected?.Authority && work.Queue == queueSnapshot;
    private bool HasQueuePrerequisiteDecision => !queuePrerequisiteDecisionRequired
        && (queuePrerequisite is null || QueuePrerequisiteSourceCurrent);
    public string QueuePrerequisiteStatus
    {
        get
        {
            if (!QueuePrerequisiteSourceCurrent)
            {
                return "Prerequisite metadata unavailable/stale. "
                    + (queuePrerequisiteDecisionRequired || queuePrerequisite is not null
                        ? "Refresh selected work and explicitly choose None or reselect an exact task; no dependency is inferred."
                        : "None (explicit no prerequisite). Refresh selected work before selecting an exact task.");
            }
            var source = queuePrerequisites.Count == 0 ? "No task metadata rows in this bounded observation."
                : "At most 50 already-observed same-session task metadata rows; omitted tasks are not scanned.";
            var choice = queuePrerequisiteDecisionRequired ? "Explicit choice required: None or select an exact task."
                : queuePrerequisite is { } option ? "Selected prerequisite: " + option.Label
                : "None (explicit no prerequisite; initial default).";
            return source + "\n" + choice + "\nEach Enqueue button creates only its named fixed read with this prerequisite. "
                + "Only exact Succeeded permits dispatch; pending, failed, cancelled or Unknown can block FIFO. "
                + "No automatic run, approval or grant. Enqueue clears the choice; choose explicitly for the next read.";
        }
    }

    public void SelectQueuePrerequisite(QueuePrerequisiteOption? option)
    {
        if (notifying || presentingQueuePrerequisites || busy || closed || option is null) { return; }
        if (!CanSelectQueuePrerequisite || !queuePrerequisites.Any(item => ReferenceEquals(item, option))) { return; }
        queuePrerequisite = option;
        queuePrerequisiteDecisionRequired = false;
        queuePrerequisiteEpoch++;
        NotifyQueuePrerequisite();
    }

    public void ChooseNoQueuePrerequisite()
    {
        if (!CanRead) { return; }
        queuePrerequisite = null;
        queuePrerequisiteDecisionRequired = false;
        queuePrerequisiteEpoch++;
        NotifyQueuePrerequisite();
    }

    private void InvalidateQueuePrerequisite(bool clearSource = false, bool requireDecision = false)
    {
        queuePrerequisiteDecisionRequired |= queuePrerequisite is not null || requireDecision;
        queuePrerequisite = null;
        queuePrerequisiteEpoch++;
        if (clearSource) { queuePrerequisites = []; queuePrerequisiteMetadataAvailable = false; }
    }

    private void ObserveQueuePrerequisites(SessionWorkSnapshot snapshot, long observedAdmission)
    {
        InvalidateQueuePrerequisite(clearSource: true);
        queuePrerequisiteAdmission = observedAdmission;
        if (snapshot.Tasks.IsDefault || snapshot.Tasks.Length > SessionWorkSnapshot.MaximumRecords
            || snapshot.Tasks.Any(task => task.Task.Request.SessionId != snapshot.Session.Authority.SessionId
                || task.Generation != snapshot.Session.Authority.Generation)) { return; }
        queuePrerequisiteMetadataAvailable = true;
        queuePrerequisites = [.. snapshot.Tasks.Select(task => new QueuePrerequisiteOption(
                task.Task.Request.TaskId, task.Task.Request.SessionId, task.Generation, task.Task.Revision,
                task.Task.State, snapshot.ObservedAt, task.Source))];
    }

    private bool QueuePrerequisiteStillCurrent(QueuePrerequisiteOption? option, long epoch) =>
        queuePrerequisiteEpoch == epoch && HasQueuePrerequisiteDecision && ReferenceEquals(queuePrerequisite, option)
        && (option is null || QueuePrerequisiteSourceCurrent
            && queuePrerequisites.Any(item => ReferenceEquals(item, option)));

    private void NotifyQueuePrerequisite()
    {
        if (!QueuePrerequisiteSourceCurrent && queuePrerequisite is not null) { InvalidateQueuePrerequisite(clearSource: true); }
        presentingQueuePrerequisites = true;
        try
        {
            OnPropertyChanged(nameof(QueuePrerequisites));
            OnPropertyChanged(nameof(SelectedQueuePrerequisite));
            OnPropertyChanged(nameof(CanSelectQueuePrerequisite));
            OnPropertyChanged(nameof(QueuePrerequisiteStatus));
            OnPropertyChanged(nameof(CanEnqueueVersion));
        }
        finally { presentingQueuePrerequisites = false; }
    }
}
