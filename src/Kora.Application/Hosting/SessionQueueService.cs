using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Application.Configuration;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

/// <summary>Manual bounded fair dispatch of four fixed registry reads. No model or effect authority.</summary>
public sealed partial class SessionQueueService : IAsyncDisposable
{
    private readonly ISessionQueueStore store;
    private readonly ISessionWorkspaceStore workspace;
    private readonly HostTaskCoordinator tasks;
    private readonly ISessionWorkspaceAccess access;
    private readonly IDeterministicVersionQueueAction action;
    private readonly SessionQueueLimits limits;
    private readonly SessionQueueConfigurationService? configuration;
    private readonly ILogger<SessionQueueService> logger;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim dispatchGate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<HostId<TaskIdentity>, (HostId<SessionIdentity> Session, ActivityContext Context)> causes = [];
    private readonly Lock causeGate = new();
    private volatile bool disposed;

    internal SessionQueueService(ISessionQueueStore store, ISessionWorkspaceStore workspace,
        HostTaskCoordinator tasks, ISessionWorkspaceAccess access, IDeterministicVersionQueueAction action,
        SessionQueueLimits limits, ILogger<SessionQueueService> logger, TimeProvider? timeProvider = null,
        SessionQueueConfigurationService? configuration = null)
    {
        this.store = store;
        this.workspace = workspace;
        this.tasks = tasks;
        this.access = access;
        this.action = action;
        this.limits = limits;
        this.configuration = configuration;
        this.logger = logger;
        time = timeProvider ?? TimeProvider.System;
    }

    private Task<T> WithLimitsAsync<T>(Func<SessionQueueLimits, Task<T>> operation, CancellationToken token,
        bool preserveCommittedResult = false) =>
        configuration is null ? operation(limits) : configuration.WithLimitsAsync(operation, token, preserveCommittedResult);

    internal Task<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
        CancellationToken token) => WithLimitsAsync(current =>
            (store as ISessionWorkStore ?? throw new InvalidOperationException("The authoritative work snapshot service is unavailable."))
                .ReadWorkAsync(session, admissionRevision, current, token).AsTask(), token);

    public async Task<SessionCommandResult> ExecuteCommandAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(admission);
        ObjectDisposedException.ThrowIf(disposed, this);
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("An exact session ID is required."));
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Queue management requires original trusted user input.");
        }
        var revision = access.ControlRevision;
        var inspect = command.Operation is SessionCommandOperation.QueueList or SessionCommandOperation.QueueStatus or SessionCommandOperation.QueuePauseStatus;
        bool Eligible() => !disposed && !lifetime.IsCancellationRequested && admission()
            && access.ControlRevision == revision && (inspect ? access.CanInspect : access.CanControl);
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        SessionQueueSnapshot? changedControl = null;
        var controlDelivered = false;
        try
        {
            token.ThrowIfCancellationRequested();
            RequireEligible(Eligible);
            var resolved = await workspace.ReadMetadataAsync(session, token).ConfigureAwait(false);
            if (resolved.Authority.SessionId != session) { throw new InvalidDataException("The exact queue session could not be resolved."); }
            RequireEligible(Eligible);
            if (inspect)
            {
                var work = await ReadWorkAsync(session, revision, token).ConfigureAwait(false);
                work.RequireSubject(session);
                var result = command.Operation is SessionCommandOperation.QueueList or SessionCommandOperation.QueuePauseStatus
                    ? new SessionCommandResult("observed", QueueDisclosure)
                    { Queue = work!.Queue, Work = work }
                    : new SessionCommandResult("observed", QueueDisclosure)
                    { Queue = work.Queue, QueueEntry = await store.ReadQueueEntryAsync(session, RequireTask(command), token).ConfigureAwait(false) };
                token.ThrowIfCancellationRequested();
                RequireEligible(Eligible);
                _ = SessionCommandResult.Serialize(result);
                root.Complete(HostOperationOutcome.Completed);
                return result;
            }
            if (command.Operation == SessionCommandOperation.QueueCancelSelectedReview)
            {
                if (!resolved.Authority.IsActive) { throw new InvalidOperationException("Only an exact active session can review pending cancellation."); }
                var result = await ReviewCancellationAsync(command, session, revision, origin, Eligible, token).ConfigureAwait(false);
                root.Complete(HostOperationOutcome.Completed);
                return result;
            }
            if (command.Operation == SessionCommandOperation.QueueRequeueExpiredReview)
            {
                if (!resolved.Authority.IsActive) { throw new InvalidOperationException("Only an exact active session can review expired replacement."); }
                var result = await ReviewRequeueAsync(command, session, revision, origin, Eligible, token).ConfigureAwait(false);
                root.Complete(HostOperationOutcome.Completed);
                return result;
            }
            if (command.Operation == SessionCommandOperation.QueueRequeueExpiredDiscard)
            {
                _ = ConsumeRequeueReview(command, session, origin, discard: true);
                root.Complete(HostOperationOutcome.Completed);
                return new("cancelled-review", "No source removal, fresh task admission or queue change. The host-issued review and fresh IDs are revoked.");
            }
            if (command.Operation == SessionCommandOperation.QueueCancelSelectedDiscard)
            {
                DiscardCancellationReview(command, session, revision, origin);
                root.Complete(HostOperationOutcome.Completed);
                return new("cancelled-review", "No pending mutation or receipt write. Selection drafts, original deadlines and queue order remain unchanged.");
            }
            if (command.Operation is SessionCommandOperation.QueueMoveUp or SessionCommandOperation.QueueMoveDown)
            {
                if (!resolved.Authority.IsActive) { throw new InvalidOperationException("Only an exact active session can review a pending move."); }
                var preview = await ReviewMoveAsync(command, session, revision, Eligible, token).ConfigureAwait(false);
                var reviewed = new SessionCommandResult("reviewed", "Review this exact pair and original deadlines. Separate exact confirmation required; no dispatch or meaningful activity renewal.")
                    { Queue = preview.Queue, QueueMoveReview = preview };
                _ = SessionCommandResult.Serialize(reviewed);
                token.ThrowIfCancellationRequested();
                RequireEligible(Eligible);
                lock (causeGate) { moveReviews.Clear(); moveReviews[session] = (preview, origin); }
                root.Complete(HostOperationOutcome.Completed);
                return reviewed;
            }
            if (command.Operation == SessionCommandOperation.QueueMoveCancel)
            {
                lock (causeGate)
                {
                    if (!moveReviews.TryGetValue(session, out var pending) || pending.Review.Id != command.QueueMoveReviewId)
                    {
                        throw new InvalidOperationException("The exact move review is no longer current; no queue order mutation occurred.");
                    }
                    moveReviews.Remove(session);
                }
                root.Complete(HostOperationOutcome.Completed);
                return new("cancelled-review", "No queue order mutation; the exact review is revoked. Input drafts and original deadlines remain unchanged.");
            }
            var moveReview = command.Operation == SessionCommandOperation.QueueMoveConfirm
                ? ConsumeMoveReview(command, session, revision, origin) : null;
            var cancelReview = command.Operation == SessionCommandOperation.QueueCancelSelectedConfirm
                ? ConsumeCancellationReview(command, session, revision, origin) : null;
            var requeueProposal = command.Operation == SessionCommandOperation.QueueRequeueExpiredConfirm
                ? ConsumeRequeueReview(command, session, origin) : ((SessionQueueRequeueReview Review, Func<bool> Eligible)?)null;
            var requeueReview = requeueProposal?.Review;
            var intent = await workspace.RecordControlIntentAsync(request, token).ConfigureAwait(false);
            SessionQueueSnapshot snapshot;
            try
            {
                var generation = new HostRevision(command.Generation);
                snapshot = command.Operation switch
                {
                    SessionCommandOperation.QueueEnqueue => await WithLimitsAsync(current => store.EnqueueAsync(request,
                        new(new(command.WorkRequestId ?? throw new InvalidOperationException("An exact request ID is required.")),
                            session, RequireTask(command), origin),
                        generation, command.QueueRevision, revision, command.DependencyTaskId is { } dependency ? new(dependency) : null,
                        current, () => Eligible() && (configuration is null || configuration.IsCurrent(current)), token,
                        command.QueueProfile, command.ExpectedDependencyRevision).AsTask(), token).ConfigureAwait(false),
                    SessionCommandOperation.QueueCancel or SessionCommandOperation.QueueRemove =>
                        await store.RemovePendingAsync(request, generation, command.QueueRevision, RequireTask(command),
                            new(command.TaskRevision), command.Operation == SessionCommandOperation.QueueCancel
                                ? SessionQueueState.Cancelled : SessionQueueState.Removed, Eligible, token).ConfigureAwait(false),
                    SessionCommandOperation.QueueClear => await store.RemovePendingAsync(request, generation,
                        command.QueueRevision, null, null, SessionQueueState.Removed, Eligible, token).ConfigureAwait(false),
                    SessionCommandOperation.QueueDispatch => await ValidateDispatchAsync(session, generation, command.QueueRevision,
                        Eligible, token).ConfigureAwait(false),
                    SessionCommandOperation.QueuePause or SessionCommandOperation.QueueResume =>
                        await store.SetDispatchPausedAsync(request, generation, command.QueueRevision,
                            command.QueueRunId ?? throw new InvalidOperationException("An exact current queue run ID is required."),
                            command.QueueControlRevision, revision, command.Operation == SessionCommandOperation.QueuePause,
                            Eligible, token).ConfigureAwait(false),
                    SessionCommandOperation.QueueMoveConfirm =>
                        await WithLimitsAsync(current => store.MovePendingAsync(request, moveReview!,
                            () => Eligible() && (configuration is null || configuration.IsCurrent(current)), token).AsTask(), token).ConfigureAwait(false),
                    SessionCommandOperation.QueueCancelSelectedConfirm =>
                        await store.CancelSelectedPendingAsync(request, cancelReview!, Eligible, token).ConfigureAwait(false),
                    SessionCommandOperation.QueueRequeueExpiredConfirm =>
                        await WithLimitsAsync(async current =>
                        {
                            var committed = await store.RequeueExpiredAsync(request, requeueReview!, current, ConfigurationRevision,
                                () => Eligible() && requeueProposal!.Value.Eligible()
                                    && (configuration is null || configuration.IsCurrent(current)), token).ConfigureAwait(false);
                            changedControl = committed;
                            return committed;
                        }, token,
                            preserveCommittedResult: true).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("The deterministic queue operation is unavailable."),
                };
                if (command.Operation is SessionCommandOperation.QueuePause or SessionCommandOperation.QueueResume or SessionCommandOperation.QueueMoveConfirm
                    or SessionCommandOperation.QueueCancelSelectedConfirm or SessionCommandOperation.QueueRequeueExpiredConfirm)
                {
                    changedControl = snapshot;
                }
            }
            catch (InvalidOperationException)
            {
                if (changedControl is null)
                {
                    await tasks.RecordOutcomeAsync(intent, HostTaskState.Denied, CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
            // Pause controls commit their terminal task and audit before publishing run-only state at the admission fence.
            if (command.Operation is not (SessionCommandOperation.QueuePause or SessionCommandOperation.QueueResume or SessionCommandOperation.QueueMoveConfirm
                or SessionCommandOperation.QueueCancelSelectedConfirm or SessionCommandOperation.QueueRequeueExpiredConfirm))
            {
                await tasks.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
            }
            if (command.Operation == SessionCommandOperation.QueueEnqueue)
            {
                lock (causeGate) { causes[RequireTask(command)] = (session, root.Activity!.Context); }
            }
            else if (command.Operation is SessionCommandOperation.QueueCancel or SessionCommandOperation.QueueRemove or SessionCommandOperation.QueueClear)
            {
                lock (causeGate)
                {
                    foreach (var retired in causes.Where(pair => pair.Value.Session == session
                        && (command.Operation == SessionCommandOperation.QueueClear || pair.Key == RequireTask(command))).Select(pair => pair.Key).ToArray())
                    {
                        causes.Remove(retired);
                    }
                }
            }
            if (cancelReview is not null)
            {
                lock (causeGate)
                {
                    foreach (var entry in cancelReview.Entries) { causes.Remove(entry.Request.TaskId); }
                }
            }
            if (requeueReview is not null)
            {
                lock (causeGate)
                {
                    causes.Remove(requeueReview.Original.Request.TaskId);
                    causes[requeueReview.FreshRequest.TaskId] = (session, root.Activity!.Context);
                }
            }
            var reply = new SessionCommandResult("committed", cancelReview is null ? QueueDisclosure :
                "All reviewed pending entries cancelled atomically; retained Cancelled history/receipts. Unselected entries and dependents remain in order. "
                + "Cancelled prerequisites block their dependents under existing policy; no global descendant scan or cascade. Pause and deadlines unchanged; no automatic dispatch or running-work termination.")
            { Queue = snapshot, QueueMoveReceipt = snapshot.MoveReceipt, QueueCancelReceipt = snapshot.CancelReceipt,
                QueueRequeueReceipt = snapshot.RequeueReceipt };
            if (requeueReview is not null) { reply = reply with { Message = RequeueDisclosure }; }
            if (command.Operation == SessionCommandOperation.QueueDispatch)
            {
                reply = reply with { QueueDispatch = await DispatchReadyAsync(revision, Eligible, token).ConfigureAwait(false),
                    Queue = await store.ReadQueueAsync(session, token).ConfigureAwait(false) };
                reply = BoundDispatchResults(reply);
            }
            if (requeueReview is null) { token.ThrowIfCancellationRequested(); }
            RequireEligible(Eligible);
            _ = SessionCommandResult.Serialize(reply);
            controlDelivered = true;
            root.Complete(HostOperationOutcome.Completed);
            return reply;
        }
        catch (OperationCanceledException)
        {
            root.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            root.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            if (changedControl is not null && !controlDelivered)
            {
                await store.HoldDispatchControlAsync(changedControl, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task<SessionQueueSnapshot> ValidateDispatchAsync(HostId<SessionIdentity> session,
        HostRevision generation, long expectedRevision, Func<bool> eligible, CancellationToken token)
    {
        var current = await store.ReadQueueAsync(session, token).ConfigureAwait(false);
        var authority = await workspace.ReadMetadataAsync(session, token).ConfigureAwait(false);
        RequireEligible(eligible);
        if (current.Generation != generation || current.Revision != expectedRevision || !authority.Authority.IsActive)
        {
            throw new InvalidOperationException("Refresh the exact active session generation and queue revision.");
        }
        return current;
    }

    private async Task<IReadOnlyList<SessionQueueDispatchReceipt>> DispatchReadyAsync(long admissionRevision,
        Func<bool> eligible, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await dispatchGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var receipts = new List<SessionQueueDispatchReceipt>();
            var running = new List<Task<SessionQueueDispatchReceipt>>();
            Exception? failure = null;
            var admittedCount = 0;
            try
            {
                // Bound each explicit management turn; remaining ready work needs another exact dispatch.
                while (admittedCount < 32 || running.Count != 0)
                {
                    RequireEligible(eligible);
                    while (admittedCount < 32)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        var work = await WithLimitsAsync<(SessionQueueEntry Entry, Func<HostActivity> Continuation, long Started)?>(async current =>
                        {
                            if (running.Count >= current.ExecutionSlots) { return null; }
                            var ready = await store.FindReadyAsync(admissionRevision, current, linked.Token).ConfigureAwait(false);
                            if (ready is null) { return null; }
                            ActivityLink[] links;
                            lock (causeGate) { links = causes.TryGetValue(ready.Request.TaskId, out var cause) ? [new(cause.Context)] : []; }
                            using var dispatch = HostActivity.BeginRoot(ready.Request, HostActivityLayer.Application, HostOperation.Runtime, links);
                            var admitted = await store.AdmitAsync(ready, admissionRevision, current,
                                () => eligible() && (configuration is null || configuration.IsCurrent(current)), linked.Token).ConfigureAwait(false);
                            admitted.Entry.Validate();
                            if (admitted.Entry.RecordVersion is not (SessionQueueEntry.CapturedActiveRecordVersion or SessionQueueEntry.FixedReadRecordVersion)
                                || admitted.Entry.ActiveBudgetMinutes != current.ActiveBudgetMinutes)
                            {
                                throw new InvalidDataException("The committed active budget is missing or differs from its admitted configuration.");
                            }
                            dispatch.Complete(HostOperationOutcome.Completed);
                            var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Tool);
                            return (admitted.Entry, continuation, admitted.StartedTimestamp);
                        }, linked.Token).ConfigureAwait(false);
                        if (work is null) { break; }
                        var observation = work.Value;
                        running.Add(Task.Run(() => ObserveAndCommitAsync(observation.Entry, observation.Continuation,
                            eligible, observation.Started, linked.Token), CancellationToken.None));
                        admittedCount++;
                    }
                    if (running.Count == 0) { break; }
                    var finished = await Task.WhenAny(running).ConfigureAwait(false);
                    running.Remove(finished);
                    var receipt = await finished.ConfigureAwait(false);
                    receipts.Add(receipt);
                    if (receipt.Entry.State == SessionQueueState.Failed) { break; }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            // Never abandon admitted callbacks after a cancellation or admission/audit failure.
            failure = await DrainAsync(running, receipts, failure).ConfigureAwait(false);
            if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
            return receipts;
        }
        finally { dispatchGate.Release(); }
    }

    internal static async Task<Exception?> DrainAsync(IReadOnlyList<Task<SessionQueueDispatchReceipt>> running,
        List<SessionQueueDispatchReceipt> receipts, Exception? failure)
    {
        foreach (var remaining in running)
        {
            try { receipts.Add(await remaining.ConfigureAwait(false)); }
            catch (Exception exception) { failure ??= exception; }
        }
        return failure;
    }

    private async Task<SessionQueueDispatchReceipt> ObserveAndCommitAsync(SessionQueueEntry admitted,
        Func<HostActivity> continuation, Func<bool> eligible, long started, CancellationToken token)
    {
        using var activity = continuation();
        try
        {
            RequireEligible(eligible);
            token.ThrowIfCancellationRequested();
            var result = time.GetElapsedTime(started) < SessionQueueLimits.ActiveBudget(admitted.ActiveBudgetMinutes!.Value)
                ? action.Observe(admitted.ReadProfile, token)
                : new CapabilityReply(CapabilityOutcome.Failed, "active-budget-expired");
            RequireEligible(eligible);
            token.ThrowIfCancellationRequested();
            var outcome = SessionQueueReadProfiles.Accepts(admitted.ReadProfile, result)
                && time.GetElapsedTime(started) < SessionQueueLimits.ActiveBudget(admitted.ActiveBudgetMinutes!.Value)
                ? SessionQueueState.Succeeded : SessionQueueState.Failed;
            var completed = await store.CompleteAsync(admitted, outcome,
                () => eligible() && !token.IsCancellationRequested && (outcome != SessionQueueState.Succeeded
                    || time.GetElapsedTime(started) < SessionQueueLimits.ActiveBudget(admitted.ActiveBudgetMinutes!.Value)),
                CancellationToken.None,
                outcome == SessionQueueState.Succeeded && admitted.Profile is not null ? result : null).ConfigureAwait(false);
            lock (causeGate) { causes.Remove(admitted.Request.TaskId); }
            RequireEligible(eligible);
            token.ThrowIfCancellationRequested();
            if (outcome == SessionQueueState.Succeeded
                && time.GetElapsedTime(started) >= SessionQueueLimits.ActiveBudget(admitted.ActiveBudgetMinutes!.Value))
            {
                throw new InvalidOperationException("The fixed read result is too late for current publication; inspect its exact retained receipt.");
            }
            activity.Complete(outcome == SessionQueueState.Succeeded ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return new(completed with { Result = null }, outcome == SessionQueueState.Succeeded ? result.Version : null,
                outcome == SessionQueueState.Succeeded && admitted.Profile is not null ? result : null);
        }
        catch
        {
            // A lost receipt or private admission leaves dispatched work for Unknown recovery.
            // Neither an SDK acknowledgement nor a late callback supplies cancellation certainty.
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private static void RequireEligible(Func<bool> eligible)
    {
        if (!eligible()) { throw new InvalidOperationException("Queue private ownership/admission changed; fresh explicit management is required."); }
    }

    private static HostId<TaskIdentity> RequireTask(SessionCommand command) =>
        new(command.TaskId ?? throw new InvalidOperationException("An exact queue task ID is required."));

    internal static SessionCommandResult BoundDispatchResults(SessionCommandResult reply)
    {
        var receipts = reply.QueueDispatch!.ToArray();
        for (var index = receipts.Length - 1; index >= 0
            && SessionCommandResult.GetSerializedSize(reply) > SessionCommand.MaximumResultBytes; index--)
        {
            if (receipts[index].Result is null) { continue; }
            receipts[index] = receipts[index] with { Result = null, ResultAvailableByExactStatus = true };
            reply = reply with { QueueDispatch = receipts };
        }
        return reply;
    }

    private const string QueueDisclosure = "Four fixed no-argument first-page local reads only; manual fair dispatch, no model/network/audio, "
        + "worker, provider, arbitrary resource, action approval or restart replay. Unknown work quarantines its session/dependents. "
        + "Admission changes require explicit removal/requeue; cancellation/removal applies only before admission. "
        + "Retained fixed-page results omitted by the batch byte limit are available through exact queue status.";

    public async ValueTask DisposeAsync()
    {
        lock (causeGate)
        {
            if (disposed) { return; }
            disposed = true;
        }
        await lifetime.CancelAsync().ConfigureAwait(false);
        await dispatchGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        lock (causeGate) { causes.Clear(); moveReviews.Clear(); cancelReviews.Clear(); requeueReviews.Clear(); }
        dispatchGate.Dispose();
        lifetime.Dispose();
    }
}
