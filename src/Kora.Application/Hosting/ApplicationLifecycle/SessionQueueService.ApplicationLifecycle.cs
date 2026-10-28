using System.Collections.Immutable;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionQueueService
{
    private sealed class LifecycleCommand(SessionQueueService owner, bool dispatch) : IDisposable
    {
        internal bool Dispatch { get; } = dispatch;
        internal Exception? Failure { get; set; }
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose()
        {
            lock (owner.causeGate)
            {
                owner.lifecycleCommands.Remove(this);
                owner.lifecycleRevision++;
                Completed.TrySetResult();
            }
        }
    }

    private readonly HashSet<LifecycleCommand> lifecycleCommands = [];
    private SessionQueueLifecycleReview? lifecycleReview;
    private bool lifecycleHeld;
    private bool lifecycleRetiring;
    private long lifecycleRevision;
    private long lifecycleEpoch;
    private DateTimeOffset lifecycleExpiresAt;
    private TaskCompletionSource lifecycleReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private LifecycleCommand EnterLifecycleCommand(SessionCommand command)
    {
        lock (causeGate)
        {
            ExpireLifecycleBarrier();
            if (lifecycleHeld) { throw new InvalidOperationException("Application lifecycle review holds queue mutation and dispatch. Cancel the review to continue."); }
            var admitted = new LifecycleCommand(this, command.Operation == SessionCommandOperation.QueueDispatch);
            lifecycleCommands.Add(admitted);
            lifecycleRevision++;
            return admitted;
        }
    }

    private bool LifecycleDispatchOpen { get { lock (causeGate) { return !lifecycleHeld; } } }

    private void ExpireLifecycleBarrier()
    {
        if (lifecycleHeld && !lifecycleRetiring && time.GetUtcNow() >= lifecycleExpiresAt)
        { ReleaseLifecycleBarrier(); }
    }

    private async Task<bool> WaitLifecycleDispatchAsync(CancellationToken token)
    {
        Task changed;
        TimeSpan remaining;
        long epoch;
        lock (causeGate)
        {
            ExpireLifecycleBarrier();
            if (lifecycleRetiring) { return false; }
            if (!lifecycleHeld) { return true; }
            changed = lifecycleReleased.Task;
            epoch = lifecycleEpoch;
            remaining = TimeSpan.FromTicks(Math.Max(0, (lifecycleExpiresAt - time.GetUtcNow()).Ticks));
        }
#pragma warning disable VSTHRD003 // The review signals this asynchronous gate without dispatcher affinity.
        try { await changed.WaitAsync(remaining, time, token).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (causeGate)
            {
                if (lifecycleEpoch == epoch && !lifecycleRetiring) { ReleaseLifecycleBarrier(); }
            }
        }
#pragma warning restore VSTHRD003
        lock (causeGate) { return !lifecycleRetiring; }
    }

    public async Task<SessionQueueLifecycleReview> ReviewApplicationLifecycleAsync(RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        var control = access.ControlRevision;
        var expires = time.GetUtcNow().AddMinutes(1);
        bool Current() => !disposed && access.CanControl && access.ControlRevision == control && admitted()
            && !token.IsCancellationRequested && time.GetUtcNow() < expires;
        long revision;
        long epoch;
        lock (causeGate)
        {
            ExpireLifecycleBarrier();
            if (!Current() || lifecycleHeld || lifecycleCommands.Any(item => !item.Dispatch))
            { throw new InvalidOperationException("Compatibility queue control is still completing or private lifecycle admission is unavailable. Review again."); }
            lifecycleHeld = true;
            lifecycleExpiresAt = expires;
            epoch = ++lifecycleEpoch;
            revision = lifecycleRevision;
        }
        try
        {
            var queues = await ReadLifecycleQueuesAsync(token).ConfigureAwait(false);
            lock (causeGate)
            {
                if (!Current() || revision != lifecycleRevision || !lifecycleHeld || epoch != lifecycleEpoch)
                { throw new InvalidOperationException("Compatibility work changed while reviewing. No work was stopped."); }
                return lifecycleReview = new(queues, revision, epoch, origin, expires, Current, ReleaseLifecycleReview);
            }
        }
        catch
        {
            lock (causeGate) { if (epoch == lifecycleEpoch) { ReleaseLifecycleBarrier(); } }
            throw;
        }
    }

    private async Task<ImmutableArray<SessionQueueSnapshot>> ReadLifecycleQueuesAsync(CancellationToken token)
    {
        var result = ImmutableArray.CreateBuilder<SessionQueueSnapshot>();
        Guid? after = null;
        do
        {
            var page = await workspace.ReadMetadataPageAsync(after, SessionPage<SessionWorkspaceEntry>.MaximumRecords, token).ConfigureAwait(false);
            foreach (var session in page.Records)
            {
                var queue = await store.ReadQueueAsync(session.Authority.SessionId, token).ConfigureAwait(false);
                if (queue.Entries.Any(item => item.State is SessionQueueState.Pending or SessionQueueState.Running))
                { result.Add(queue); }
            }
            after = page.Next;
        } while (after is not null);
        return result.ToImmutable();
    }

    public bool IsApplicationLifecycleCurrent(SessionQueueLifecycleReview review)
    {
        lock (causeGate) { return lifecycleHeld && lifecycleEpoch == review.Epoch && ReferenceEquals(lifecycleReview, review)
            && !review.ReleasePending && (review.Consumed || lifecycleRevision == review.Revision) && review.Current(); }
    }

    public async Task ConfirmApplicationLifecycleAsync(SessionQueueLifecycleReview review, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(review.Origin);
        var current = await ReadLifecycleQueuesAsync(token).ConfigureAwait(false);
        LifecycleCommand[] commands;
        lock (causeGate)
        {
            if (!IsApplicationLifecycleCurrent(review) || review.Consumed || review.Revision != lifecycleRevision
                || !current.Select(item => (item.SessionId, item.Generation, item.Revision))
                    .SequenceEqual(review.Queues.Select(item => (item.SessionId, item.Generation, item.Revision))))
            { throw new InvalidOperationException("The exact compatibility work review changed. No work was stopped."); }
            review.Consumed = true;
            review.Joining = true;
            lifecycleRetiring = true;
            lifecycleReleased.TrySetResult();
            commands = lifecycleCommands.ToArray();
        }
        Exception? retirementFailure = null;
        try { await RetireLifecyclePendingAsync(review, token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { retirementFailure = exception; }
        try
        {
#pragma warning disable VSTHRD003 // Finite admitted read commands retain ownership through their actual callbacks and terminal commits.
            await Task.WhenAll(commands.Select(item => item.Completed.Task)).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (retirementFailure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(retirementFailure).Throw(); }
            if (commands.Any(item => item.Failure is not null))
            { throw new InvalidOperationException("An admitted compatibility read failed before its terminal release was confirmed."); }
            var terminal = ImmutableArray.CreateBuilder<SessionQueueSnapshot>();
            foreach (var queue in review.Queues)
            {
                var entries = new List<SessionQueueEntry>();
                foreach (var original in queue.Entries.Where(item => item.State is SessionQueueState.Pending or SessionQueueState.Running))
                {
                    var actual = await store.ReadQueueEntryAsync(queue.SessionId, original.Request.TaskId, token).ConfigureAwait(false);
                    if (actual is null || actual.Request != original.Request || actual.Generation != original.Generation
                        || actual.IsPending || actual.IsCurrent)
                    { throw new InvalidOperationException("The exact compatibility terminal readback is unconfirmed."); }
                    entries.Add(actual);
                }
                terminal.Add(queue with { Entries = entries });
            }
            review.TerminalQueues = terminal.ToImmutable();
            var remaining = await ReadLifecycleQueuesAsync(token).ConfigureAwait(false);
            if (!IsApplicationLifecycleCurrent(review) || remaining.Length != 0)
            { throw new InvalidOperationException("Compatibility work release is unconfirmed. No application effect is permitted."); }
            review.Joined = true;
        }
        finally
        {
            lock (causeGate)
            {
                review.Joining = false;
                if (review.ReleasePending) { ReleaseLifecycleReview(review); }
            }
        }
    }

    private async Task RetireLifecyclePendingAsync(SessionQueueLifecycleReview review, CancellationToken token)
    {
        foreach (var queue in review.Queues.Where(item => item.Entries.Any(entry => entry.State == SessionQueueState.Pending)))
        {
            var request = new HostRequest(new(Guid.NewGuid()), queue.SessionId, new(Guid.NewGuid()), review.Origin);
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var intent = await workspace.RecordControlIntentAsync(request, token).ConfigureAwait(false);
            var stopped = await store.RemovePendingAsync(request, queue.Generation, queue.Revision, null, null,
                SessionQueueState.Cancelled, () => IsApplicationLifecycleCurrent(review), token).ConfigureAwait(false);
            if (stopped.Entries.Any(item => item.State == SessionQueueState.Pending))
            { throw new InvalidOperationException("Compatibility pending withdrawal is unconfirmed."); }
            var terminal = await tasks.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
            var readback = await workspace.ReadTaskAsync(queue.SessionId, request.TaskId, token).ConfigureAwait(false);
            if (readback?.Task != terminal)
            { throw new InvalidOperationException("Compatibility cancellation terminal readback is unconfirmed."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
    }

    private void ReleaseLifecycleReview(SessionQueueLifecycleReview review)
    {
        lock (causeGate)
        {
            if (!ReferenceEquals(lifecycleReview, review) || lifecycleEpoch != review.Epoch) { return; }
            if (review.Joining) { review.ReleasePending = true; return; }
            lifecycleReview = null;
            ReleaseLifecycleBarrier();
        }
    }

    private void ReleaseLifecycleBarrier()
    {
        lifecycleHeld = false;
        lifecycleReview = null;
        lifecycleEpoch++;
        lifecycleRetiring = false;
        lifecycleReleased.TrySetResult();
        lifecycleReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
