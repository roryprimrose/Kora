using System.Collections.Immutable;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionQueueService
{
    private SessionDiagnosticsPlanReview? diagnosticsReview;
    private Func<bool> diagnosticsAdmission = static () => false;
    private long diagnosticsEpoch;
    private bool applyingDiagnostics;

    public void RetireDiagnosticsPlan(SessionDiagnosticsPlanReview review)
    {
        lock (causeGate)
        {
            if (!ReferenceEquals(diagnosticsReview, review)) { return; }
            diagnosticsReview = null;
            diagnosticsAdmission = static () => false;
            diagnosticsEpoch++;
        }
    }

    public async Task<SessionDiagnosticsPlanReview> ReviewDiagnosticsPlanAsync(SessionQueueSnapshot expected,
        IReadOnlyList<SessionQueueReadProfile> selected, SessionDiagnosticsPlanPrerequisite prerequisite,
        RequestOrigin origin, Func<bool> admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(admission);
        RequireDiagnosticsOrigin(origin);
        long epoch;
        lock (causeGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (applyingDiagnostics) { throw new InvalidOperationException("A diagnostics plan is already applying."); }
            diagnosticsReview = null;
            diagnosticsAdmission = static () => false;
            epoch = ++diagnosticsEpoch;
        }
        // Admit the complete finite selection before reading authority, including the final item.
        var profiles = SelectDiagnosticsProfiles(selected, prerequisite);
        var admissionRevision = access.ControlRevision;
        bool Eligible() => DiagnosticsCurrent(epoch, admissionRevision) && admission() && !token.IsCancellationRequested;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), expected.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request);
        try
        {
            var review = await WithLimitsAsync(async current =>
            {
                var snapshot = await ReadDiagnosticsQueueAsync(expected.SessionId, Eligible, token).ConfigureAwait(false);
                RequireDiagnosticsSnapshot(snapshot, admissionRevision);
                if (!SameDiagnosticsQueue(expected, snapshot))
                {
                    throw new InvalidOperationException("Refresh the exact selected queue generation, revisions and original deadlines.");
                }
                var result = new SessionDiagnosticsPlanReview(snapshot, profiles, admissionRevision, current, ConfigurationRevision);
                RequireDiagnosticsCapacity(result, snapshot, profiles.Length);
                return result;
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            RequireEligible(Eligible);
            lock (causeGate)
            {
                RequireEligible(Eligible);
                diagnosticsReview = review;
                diagnosticsAdmission = () => admission() && !token.IsCancellationRequested;
            }
            activity.Complete(HostOperationOutcome.Completed);
            return review;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public async Task<SessionDiagnosticsPlanOutcome> ConfirmDiagnosticsPlanAsync(SessionDiagnosticsPlanReview review,
        RequestOrigin origin, Func<bool> admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(admission);
        RequireDiagnosticsOrigin(origin);
        long epoch;
        Func<bool> originalAdmission;
        lock (causeGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (applyingDiagnostics || !ReferenceEquals(diagnosticsReview, review))
            {
                throw new InvalidOperationException("The exact one-shot diagnostics review was retired or consumed. Review again.");
            }
            diagnosticsReview = null;
            originalAdmission = diagnosticsAdmission;
            diagnosticsAdmission = static () => false;
            epoch = diagnosticsEpoch;
            applyingDiagnostics = true;
        }
        bool Eligible() => DiagnosticsCurrent(epoch, review.AdmissionRevision)
            && originalAdmission() && admission() && !token.IsCancellationRequested;
        var outcomes = review.Profiles.Select(profile => new SessionDiagnosticsPlanItem(profile,
            SessionDiagnosticsPlanItemState.NotAttempted)).ToArray();
        var index = 0;
        var attempted = false;
        SessionDiagnosticsPlanItemState? stoppedBy = null;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Queue.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request);
        try
        {
            _ = SelectDiagnosticsProfiles(review.Profiles, review.Prerequisite);
            RequireDiagnosticsSnapshot(review.Queue, review.AdmissionRevision);
            var expected = review.Queue;
            // Full preflight before any effect, and again before EVERY authoritative single enqueue.
            await PreflightAsync(expected, outcomes.Length).ConfigureAwait(false);
            for (; index < outcomes.Length; index++)
            {
                attempted = false;
                await PreflightAsync(expected, outcomes.Length - index).ConfigureAwait(false);
                var work = new HostRequest(new(Guid.NewGuid()), expected.SessionId, new(Guid.NewGuid()), origin);
                outcomes[index] = outcomes[index] with { Request = work };
                var command = new SessionCommand(SessionCommandOperation.QueueEnqueue, expected.SessionId.Value,
                    expected.Generation.Value)
                {
                    QueueRevision = expected.Revision,
                    WorkRequestId = work.RequestId.Value,
                    TaskId = work.TaskId.Value,
                    QueueProfile = outcomes[index].Profile,
                    // Intentionally no native row prerequisite, target, arguments or dispatch.
                };
                attempted = true;
                _ = await ExecuteCommandCoreAsync(command, origin, Eligible, token, review, committed =>
                {
                    var entry = committed.Entries.SingleOrDefault(row => row.Request == work);
                    if (entry is null || entry.ReadProfile != outcomes[index].Profile || entry.Dependency is not null
                        || entry.Target is not null || !entry.IsPending || entry.Generation != expected.Generation
                        || entry.RunId != expected.DispatchControl!.RunId || entry.AdmissionRevision != review.AdmissionRevision
                        || entry.PendingLifetimeMinutes != review.Limits.PendingLifetimeMinutes)
                    {
                        throw new InvalidDataException("The exact newly committed fixed read receipt is unavailable.");
                    }
                    entry.Validate();
                    outcomes[index] = outcomes[index] with { CommittedEntry = entry, CommittedQueueRevision = committed.Revision };
                    RequireDiagnosticsSnapshot(committed, review.AdmissionRevision);
                    if (committed.SessionId != expected.SessionId || committed.Generation != expected.Generation
                        || committed.DispatchControl != expected.DispatchControl || committed.Revision <= expected.Revision)
                    {
                        throw new InvalidDataException("The returned durable queue/control receipt is not the exact reviewed enqueue.");
                    }
                    expected = committed;
                }).ConfigureAwait(false);
                outcomes[index] = outcomes[index] with { State = SessionDiagnosticsPlanItemState.Accepted };
            }
        }
        catch (OperationCanceledException) { Stop(attempted ? SessionDiagnosticsPlanItemState.Unavailable : SessionDiagnosticsPlanItemState.Cancelled); }
        catch (InvalidOperationException) { Stop(attempted ? SessionDiagnosticsPlanItemState.Unavailable : SessionDiagnosticsPlanItemState.Conflict); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            Stop(SessionDiagnosticsPlanItemState.Unavailable);
        }
        finally { lock (causeGate) { applyingDiagnostics = false; } }
        activity.Complete(stoppedBy is null ? HostOperationOutcome.Completed
            : stoppedBy == SessionDiagnosticsPlanItemState.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return new(review.Id, [.. outcomes], stoppedBy);

        void Stop(SessionDiagnosticsPlanItemState state)
        {
            stoppedBy = state;
            if (index < outcomes.Length) { outcomes[index] = outcomes[index] with { State = state }; }
        }

        Task PreflightAsync(SessionQueueSnapshot expected, int remaining) => WithLimitsAsync(async current =>
        {
            token.ThrowIfCancellationRequested();
            RequireEligible(Eligible);
            if (current != review.Limits || ConfigurationRevision != review.ConfigurationRevision)
            {
                throw new InvalidOperationException("Reviewed queue limits changed; no automatic resizing or retry.");
            }
            var observed = await ReadDiagnosticsQueueAsync(expected.SessionId, Eligible, token).ConfigureAwait(false);
            RequireDiagnosticsSnapshot(observed, review.AdmissionRevision);
            if (!SameDiagnosticsQueue(expected, observed))
            {
                throw new InvalidOperationException("Exact queue, generation, run/control or original deadlines changed.");
            }
            RequireDiagnosticsCapacity(review, observed, remaining);
            token.ThrowIfCancellationRequested();
            RequireEligible(Eligible);
            return true;
        }, token);
    }

    private bool DiagnosticsCurrent(long epoch, long admissionRevision)
    {
        lock (causeGate)
        {
            return !disposed && diagnosticsEpoch == epoch
                && access.CanControl && access.CanInspect && access.ControlRevision == admissionRevision;
        }
    }

    private async Task<SessionQueueSnapshot> ReadDiagnosticsQueueAsync(HostId<SessionIdentity> session,
        Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireEligible(eligible);
        var metadata = await workspace.ReadMetadataAsync(session, token).ConfigureAwait(false);
        var snapshot = await store.ReadQueueAsync(session, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        RequireEligible(eligible);
        if (metadata.Authority.SessionId != session || !metadata.Authority.IsActive
            || snapshot.SessionId != session || metadata.Authority.Generation != snapshot.Generation)
        {
            throw new InvalidOperationException("The exact selected active diagnostics session retired or changed generation.");
        }
        return snapshot;
    }

    private static ImmutableArray<SessionQueueReadProfile> SelectDiagnosticsProfiles(
        IReadOnlyList<SessionQueueReadProfile> selected, SessionDiagnosticsPlanPrerequisite prerequisite)
    {
        if (prerequisite != SessionDiagnosticsPlanPrerequisite.None || selected.Count is < 1 or > 4
            || selected.Distinct().Count() != selected.Count
            || selected.Any(profile => profile is not (SessionQueueReadProfile.Version or SessionQueueReadProfile.Capabilities
                or SessionQueueReadProfile.Readiness or SessionQueueReadProfile.Runtimes)))
        {
            throw new InvalidDataException("Choose one to four unique fixed no-argument profiles and None for every prerequisite.");
        }
        foreach (var profile in selected) { SessionQueueReadProfiles.Validate(profile); }
        return [.. selected.OrderBy(profile => profile)];
    }

    private static void RequireDiagnosticsSnapshot(SessionQueueSnapshot snapshot, long admissionRevision)
    {
        snapshot.SessionId.Validate();
        if (snapshot.Generation.Value <= 0 || snapshot.Revision < 0
            || snapshot.DispatchControl is not { } control || control.RunId == Guid.Empty
            || control.Generation != snapshot.Generation || control.Revision < 0 || control.ControlRevision < 0 || control.Held
            || control.Revision > 0 && control.ControlRevision != admissionRevision
            || snapshot.Entries.Count > SessionQueueLimits.MaximumPendingPerSession + 1
            || snapshot.Entries.Select(entry => entry.Request.TaskId).Distinct().Count() != snapshot.Entries.Count)
        {
            throw new InvalidDataException("The complete queue/control observation is unavailable or invalid.");
        }
        foreach (var entry in snapshot.Entries)
        {
            entry.Validate();
            if (entry.Request.SessionId != snapshot.SessionId || entry.Generation != snapshot.Generation
                || entry.RunId != control.RunId)
            {
                throw new InvalidDataException("A queue item belongs to a foreign session, generation or run.");
            }
        }
    }

    private static bool SameDiagnosticsQueue(SessionQueueSnapshot expected, SessionQueueSnapshot observed) =>
        expected.SessionId == observed.SessionId && expected.Generation == observed.Generation
        && expected.Revision == observed.Revision && expected.DispatchControl == observed.DispatchControl
        && expected.Entries.SequenceEqual(observed.Entries);

    private static void RequireDiagnosticsCapacity(SessionDiagnosticsPlanReview review, SessionQueueSnapshot snapshot, int count)
    {
        if (SessionQueueCapacity.PendingCount(snapshot.Entries, snapshot.DispatchControl!.RunId) + count > review.Limits.PendingPerSession)
        {
            throw new InvalidOperationException("The complete selected plan exceeds confirmed pending capacity; select a smaller subset explicitly.");
        }
    }

    private static void RequireDiagnosticsOrigin(RequestOrigin origin)
    {
        if (origin != RequestOrigin.LocalUi || HostActivity.Current is { Request.Origin: not RequestOrigin.LocalUi })
        {
            throw new InvalidOperationException("Diagnostics plans require explicit original native user review and separate confirmation.");
        }
    }
}
