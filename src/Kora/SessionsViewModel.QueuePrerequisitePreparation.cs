using System.Diagnostics;
using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal sealed record PreparedQueuePrerequisite(SessionWorkSnapshot Snapshot,
        QueuePrerequisiteOption? Candidate, Func<bool> Current, ActivityContext? Cause);

    private PreparedQueuePrerequisite? preparedQueuePrerequisite;
    private long prerequisitePreparationEpoch;
    private bool preparingPrerequisite;
    private string prerequisitePreparationStatus = "No prepared prerequisite. Existing selections are unchanged.";

    public string QueuePrerequisitePreparationStatus
    {
        get
        {
            CheckPreparedQueuePrerequisite();
            return prerequisitePreparationStatus;
        }
    }
    public bool CanApplyPreparedQueuePrerequisite
    {
        get
        {
            CheckPreparedQueuePrerequisite();
            return CanRead && !preparingPrerequisite && preparedQueuePrerequisite is not null;
        }
    }
    public bool CanCancelPreparedQueuePrerequisite => !closed && (preparingPrerequisite || preparedQueuePrerequisite is not null);
    public PreparedQueuePrerequisite? PreparedQueuePrerequisiteChoice
    {
        get { CheckPreparedQueuePrerequisite(); return preparedQueuePrerequisite; }
    }

    internal Func<bool> CapturePrerequisitePreparationEligibility()
    {
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        var work = workSnapshot;
        var queue = queueSnapshot;
        var subject = selected;
        var prerequisiteEpoch = queuePrerequisiteEpoch;
        var source = queuePrerequisites;
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var profiles = diagnosticsProfiles.ToArray();
        var diagnosticsReview = diagnosticsPlanReview;
        var capability = queueCapability;
        return () => !closed && access.CanInspect && access.CanControl && access.ControlRevision == revision
            && selectionEpoch == epoch && ReferenceEquals(selected, subject)
            && ReferenceEquals(workSnapshot, work) && ReferenceEquals(queueSnapshot, queue)
            && queuePrerequisiteEpoch == prerequisiteEpoch && ReferenceEquals(queuePrerequisites, source)
            && Interlocked.Read(ref composerBufferRevision) == buffer
            && diagnosticsProfiles.SetEquals(profiles) && ReferenceEquals(diagnosticsPlanReview, diagnosticsReview)
            && ReferenceEquals(queueCapability, capability);
    }

    internal async Task<bool> PrepareQueuePrerequisiteAsync(SessionQueuePrerequisitePrepareCommand command,
        RequestOrigin origin, Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token)
    {
        RetirePreparedQueuePrerequisite();
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || command.Error is not null
            || command.Help || command.SessionId == Guid.Empty || command.TaskId == Guid.Empty)
        { throw new InvalidOperationException("An exact original-user preparation request is required."); }
        if (!CanRead || preparingPrerequisite || draftRecoveryCancellation is not null)
        { throw new InvalidOperationException("Finish the current native operation before preparing an exact prerequisite."); }
        var epoch = prerequisitePreparationEpoch;
        var original = CapturePrerequisitePreparationEligibility();
        bool Original() => epoch == prerequisitePreparationEpoch && original() && admitted() && nativeAdmission()
            && !token.IsCancellationRequested;
        preparingPrerequisite = true;
        busy = true;
        ActivityLink[] links = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), new(command.SessionId),
            viewer.TaskId, origin), HostActivityLayer.Desktop, HostOperation.Presentation, links: links);
        try
        {
            if (!Original()) { throw new OperationCanceledException("Original preparation admission changed."); }
            if (!await ProtectComposerAsync()) { activity.Complete(HostOperationOutcome.Cancelled); return false; }
            if (!Original()) { throw new OperationCanceledException("Original composer/navigation admission changed."); }
            var body = composerBody;
            bool Current() => Original() && string.Equals(composerBody, body, StringComparison.Ordinal);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            var snapshot = await service.ReadWorkAsync(new(command.SessionId), cancellation.Token);
            var options = ReadQueuePrerequisiteOptions(snapshot)
                ?? throw new InvalidDataException("Prerequisite metadata is missing, conflicting or exceeds the 50 nonqueue/50 queued bound.");
            var candidate = command.TaskId is { } task
                ? options.SingleOrDefault(option => option.TaskId.Value == task)
                    ?? throw new InvalidDataException("The exact requested task is not in the admitted bounded options. No extra scan or None fallback.")
                : null;
            if (!Current() || !snapshot.Session.Authority.IsActive)
            { throw new OperationCanceledException("The exact preparation source is unavailable or changed."); }
            if (selected?.Authority.SessionId.Value == command.SessionId && workSnapshot is { } existing
                && !PrerequisiteSourcesEqual(existing, snapshot))
            { throw new InvalidOperationException("The selected source changed; explicitly refresh before requesting fresh preparation."); }
            preparedQueuePrerequisite = new(snapshot, candidate, Current, activity.Activity?.Context);
            prerequisitePreparationStatus = "PREPARATION ONLY. No choice changed.\n"
                + (candidate?.Label ?? "None (explicit no prerequisite) | session " + command.SessionId.ToString("D")
                    + " | generation " + snapshot.Session.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
                    + " | observed UTC " + snapshot.ObservedAt.ToString("O", CultureInfo.InvariantCulture))
                + "\nQueue revision " + snapshot.Queue.Revision.ToString(CultureInfo.InvariantCulture)
                + " | private control revision " + access.ControlRevision.ToString(CultureInfo.InvariantCulture)
                + "\nAt most 50 nonqueue and 50 already-observed queued same-session metadata rows; no history scan."
                + "\nApply prepared choice is a separate one-shot gesture. It selects this exact session and prerequisite; "
                + "a different session replaces its previous native work presentation only on Apply. Cancel preserves current selections."
                + "\n" + SessionQueuePrerequisitePrepareCommand.Boundary
                + "\nEach existing Enqueue button creates only its named fixed read with the applied prerequisite. "
                + "Pending, failed, cancelled or Unknown can block FIFO; enqueue clears the choice.";
            activity.Complete(HostOperationOutcome.Completed);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RetirePreparedQueuePrerequisite();
            prerequisitePreparationStatus = "Preparation unavailable/stale: " + exception.GetType().Name
                + ". Existing selections unchanged; request fresh exact preparation. No fallback, enqueue or dispatch.";
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            return false;
        }
        finally
        {
            preparingPrerequisite = false;
            busy = false;
            Notify();
            if (closed) { lifetime.Dispose(); }
        }
    }

    public async Task ApplyPreparedQueuePrerequisiteAsync(PreparedQueuePrerequisite? proposal)
    {
        if (notifying || preparingPrerequisite || !CanRead) { return; }
        if (proposal is null || !ReferenceEquals(proposal, preparedQueuePrerequisite))
        {
            prerequisitePreparationStatus = "Apply rejected: this exact proposal is absent or replaced. Existing choices unchanged; no newer proposal was applied.";
            NotifyPreparedQueuePrerequisite();
            return;
        }
        if (!proposal.Current())
        {
            RetirePreparedQueuePrerequisite();
            NotifyPreparedQueuePrerequisite();
            return;
        }
        // Consume before awaiting: a stale/duplicate click cannot bind a subsequent proposal.
        preparedQueuePrerequisite = null;
        preparingPrerequisite = true;
        busy = true;
        ActivityLink[] links = proposal.Cause is { } cause ? [new(cause)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), proposal.Snapshot.Session.Authority.SessionId,
            viewer.TaskId, RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Presentation, links: links);
        try
        {
            var observed = await service.ReadWorkAsync(proposal.Snapshot.Session.Authority.SessionId, lifetime.Token);
            if (!proposal.Current() || !PrerequisiteSourcesEqual(proposal.Snapshot, observed))
            { throw new OperationCanceledException("The exact observed prerequisite, queue or source changed before Apply."); }
            notifying = true;
            try
            {
                if (selected?.Authority.SessionId != proposal.Snapshot.Session.Authority.SessionId)
                {
                    ClearSelection();
                    LeaveListSearch();
                    sessionFilter = SessionListFilter.All;
                    selected = proposal.Snapshot.Session;
                    sessions = new([selected], null);
                    nameDraft = selected.Metadata?.Name.Value ?? string.Empty;
                    historySessionId = selected.Authority.SessionId.Value.ToString("D");
                    ApplyWork(proposal.Snapshot, access.ControlRevision);
                    OnPropertyChanged(nameof(SessionFilter));
                }
                else if (!QueuePrerequisiteSourceCurrent)
                {
                    ApplyWork(proposal.Snapshot, access.ControlRevision);
                }
                var exact = proposal.Candidate is { } candidate
                    ? queuePrerequisites.Single(option => option with { ObservedAt = default } == candidate with { ObservedAt = default })
                    : null;
                ApplyQueuePrerequisiteChoice(exact);
            }
            finally { notifying = false; }
            prerequisitePreparationStatus = "Prepared exact choice applied once. Nothing enqueued. Use a separately named fixed Enqueue action.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            prerequisitePreparationStatus = "Prepared choice not applied: " + exception.GetType().Name
                + ". Existing selections unchanged. Request fresh exact preparation; no retry or fallback.";
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            preparingPrerequisite = false;
            busy = false;
            Notify();
            if (closed) { lifetime.Dispose(); }
        }
    }

    public void CancelPreparedQueuePrerequisite()
    {
        RetirePreparedQueuePrerequisite();
        NotifyPreparedQueuePrerequisite();
    }

    private void RetirePreparedQueuePrerequisite()
    {
        prerequisitePreparationEpoch++;
        preparedQueuePrerequisite = null;
        prerequisitePreparationStatus = "Preparation retired/cancelled. Existing selections unchanged; no choice applied.";
    }

    private void CheckPreparedQueuePrerequisite()
    {
        if (preparedQueuePrerequisite is { } proposal && !proposal.Current()) { RetirePreparedQueuePrerequisite(); }
    }

    private void NotifyPreparedQueuePrerequisite()
    {
        CheckPreparedQueuePrerequisite();
        OnPropertyChanged(nameof(QueuePrerequisitePreparationStatus));
        OnPropertyChanged(nameof(CanApplyPreparedQueuePrerequisite));
        OnPropertyChanged(nameof(CanCancelPreparedQueuePrerequisite));
        OnPropertyChanged(nameof(PreparedQueuePrerequisiteChoice));
    }

    private static bool PrerequisiteSourcesEqual(SessionWorkSnapshot left, SessionWorkSnapshot right) =>
        left.Session == right.Session
        && left.Queue.SessionId == right.Queue.SessionId && left.Queue.Generation == right.Queue.Generation
        && left.Queue.Revision == right.Queue.Revision && left.Queue.DispatchControl == right.Queue.DispatchControl
        && left.Queue.Entries.SequenceEqual(right.Queue.Entries)
        && left.Tasks.SequenceEqual(right.Tasks)
        && left.QueueRecords.SequenceEqual(right.QueueRecords)
        && ReadQueuePrerequisiteOptions(right) is not null;
}
