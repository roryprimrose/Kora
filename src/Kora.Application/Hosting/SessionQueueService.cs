using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Application.Configuration;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

/// <summary>Manual bounded fair dispatch of the existing fixed local-version action. No model or effect authority.</summary>
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

    private Task<T> WithLimitsAsync<T>(Func<SessionQueueLimits, Task<T>> operation, CancellationToken token) =>
        configuration is null ? operation(limits) : configuration.WithLimitsAsync(operation, token);

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
        var inspect = command.Operation is SessionCommandOperation.QueueList or SessionCommandOperation.QueueStatus;
        bool Eligible() => !disposed && !lifetime.IsCancellationRequested && admission()
            && access.ControlRevision == revision && (inspect ? access.CanInspect : access.CanControl);
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        try
        {
            token.ThrowIfCancellationRequested();
            RequireEligible(Eligible);
            var resolved = await workspace.ReadMetadataAsync(session, token).ConfigureAwait(false);
            if (resolved.Authority.SessionId != session) { throw new InvalidDataException("The exact queue session could not be resolved."); }
            RequireEligible(Eligible);
            if (inspect)
            {
                var work = command.Operation == SessionCommandOperation.QueueList
                    ? await ReadWorkAsync(session, revision, token).ConfigureAwait(false) : null;
                work?.RequireSubject(session);
                var result = command.Operation == SessionCommandOperation.QueueList
                    ? new SessionCommandResult("observed", QueueDisclosure)
                    { Queue = work!.Queue, Work = work }
                    : new SessionCommandResult("observed", QueueDisclosure)
                    { QueueEntry = await store.ReadQueueEntryAsync(session, RequireTask(command), token).ConfigureAwait(false) };
                token.ThrowIfCancellationRequested();
                RequireEligible(Eligible);
                _ = SessionCommandResult.Serialize(result);
                root.Complete(HostOperationOutcome.Completed);
                return result;
            }
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
                        current, () => Eligible() && (configuration is null || configuration.IsCurrent(current)), token).AsTask(), token).ConfigureAwait(false),
                    SessionCommandOperation.QueueCancel or SessionCommandOperation.QueueRemove =>
                        await store.RemovePendingAsync(request, generation, command.QueueRevision, RequireTask(command),
                            new(command.TaskRevision), command.Operation == SessionCommandOperation.QueueCancel
                                ? SessionQueueState.Cancelled : SessionQueueState.Removed, Eligible, token).ConfigureAwait(false),
                    SessionCommandOperation.QueueClear => await store.RemovePendingAsync(request, generation,
                        command.QueueRevision, null, null, SessionQueueState.Removed, Eligible, token).ConfigureAwait(false),
                    SessionCommandOperation.QueueDispatch => await ValidateDispatchAsync(session, generation, command.QueueRevision,
                        Eligible, token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("The deterministic queue operation is unavailable."),
                };
            }
            catch (InvalidOperationException)
            {
                await tasks.RecordOutcomeAsync(intent, HostTaskState.Denied, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            await tasks.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
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
            var reply = new SessionCommandResult("committed", QueueDisclosure) { Queue = snapshot };
            if (command.Operation == SessionCommandOperation.QueueDispatch)
            {
                reply = reply with { QueueDispatch = await DispatchReadyAsync(revision, Eligible, token).ConfigureAwait(false),
                    Queue = await store.ReadQueueAsync(session, token).ConfigureAwait(false) };
            }
            RequireEligible(Eligible);
            _ = SessionCommandResult.Serialize(reply);
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
                            var started = time.GetTimestamp();
                            var admitted = await store.AdmitAsync(ready, admissionRevision, current,
                                () => eligible() && (configuration is null || configuration.IsCurrent(current)), linked.Token).ConfigureAwait(false);
                            dispatch.Complete(HostOperationOutcome.Completed);
                            var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Tool);
                            return (admitted, continuation, started);
                        }, linked.Token).ConfigureAwait(false);
                        if (work is null) { break; }
                        var observation = work.Value;
                        running.Add(Task.Run(() => ObserveAndCommitAsync(observation.Entry, observation.Continuation,
                            eligible, observation.Started), CancellationToken.None));
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
        Func<HostActivity> continuation, Func<bool> eligible, long started)
    {
        using var activity = continuation();
        try
        {
            RequireEligible(eligible);
            var result = action.Observe(lifetime.Token);
            RequireEligible(eligible);
            var outcome = result.Outcome == CapabilityOutcome.Succeeded
                && time.GetElapsedTime(started) < SessionQueuePolicy.ActiveDeadline
                ? SessionQueueState.Succeeded : SessionQueueState.Failed;
            var completed = await store.CompleteAsync(admitted, outcome, eligible, CancellationToken.None).ConfigureAwait(false);
            lock (causeGate) { causes.Remove(admitted.Request.TaskId); }
            activity.Complete(outcome == SessionQueueState.Succeeded ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return new(completed, result.Version);
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

    private const string QueueDisclosure = "Fixed local application-version work only; manual fair dispatch, no model/network/audio, "
        + "worker, provider, arbitrary resource, action approval or restart replay. Unknown work quarantines its session/dependents. "
        + "Admission changes require explicit removal/requeue; cancellation/removal applies only before admission.";

    public async ValueTask DisposeAsync()
    {
        lock (causeGate)
        {
            if (disposed) { return; }
            disposed = true;
        }
        await lifetime.CancelAsync().ConfigureAwait(false);
        await dispatchGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        lock (causeGate) { causes.Clear(); }
        dispatchGate.Dispose();
        lifetime.Dispose();
    }
}
