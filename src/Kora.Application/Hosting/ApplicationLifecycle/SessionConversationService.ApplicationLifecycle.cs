using System.Collections.Immutable;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private sealed class LifecycleSubmission(SessionConversationService owner, SessionWorkspaceEntry target, string description) : IDisposable
    {
        internal SessionWorkspaceEntry Target { get; } = target;
        internal string Description { get; } = description;
        internal readonly TaskCompletionSource Completed = NewSignal();
        public void Dispose()
        {
            lock (owner.cancellationGate)
            {
                owner.lifecycleSubmissions.Remove(this);
                owner.lifecycleRevision++;
                Completed.TrySetResult();
            }
        }
    }

    private readonly HashSet<LifecycleSubmission> lifecycleSubmissions = [];
    private ApplicationLifecycleReview? lifecycleReview;
    private bool lifecycleHeld;
    private bool lifecycleUnconfirmed;
    private long lifecycleRevision;

    private void ExpireLifecycleBarrier()
    {
        if (lifecycleReview is { Consumed: false } review && time.GetUtcNow() >= review.ExpiresAt)
        { review.Dispose(); }
    }

    private LifecycleSubmission EnterLifecycleSubmission(SessionWorkspaceEntry target, string description)
    {
        lock (cancellationGate)
        {
            RequireConversationDispatchOpen(target.Authority.SessionId);
            var submission = new LifecycleSubmission(this, target, description);
            lifecycleSubmissions.Add(submission);
            lifecycleRevision++;
            return submission;
        }
    }

    public async Task<ApplicationLifecycleReview> ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation operation,
        RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        RequireOriginalSource(origin);
        ArgumentNullException.ThrowIfNull(admitted);
        if (!Enum.IsDefined(operation)) { throw new ArgumentOutOfRangeException(nameof(operation)); }
        var control = access.ControlRevision;
        var expires = time.GetUtcNow().AddMinutes(1);
        bool SourceCurrent() => !token.IsCancellationRequested && admitted() && access.CanControl
            && access.ControlRevision == control && time.GetUtcNow() < expires;
        ApplicationLifecycleReview review;
        lock (cancellationGate)
        {
            ExpireLifecycleBarrier();
            if (!SourceCurrent() || lifecycleHeld || pendingClosed || lifecycleUnconfirmed)
            { throw new InvalidOperationException("Application work review is unavailable or earlier work has unconfirmed release. No work was stopped."); }
            var running = runningConversations.Values.Select(item => item.Status).ToImmutableArray();
            var pending = conversationLanes.Select(item => Snapshot(item.Key, item.Value)).ToImmutableArray();
            var entries = conversationLanes.Values.SelectMany(item => item.Entries).ToArray();
            var represented = entries.Length + running.Count(item => !entries.Any(entry =>
                entry.Retained?.Message.Request == item.Target.Request));
            if (entries.Any(item => item.Retained is null
                || item.Phase == ConversationPendingPhase.Dispatching
                    && !running.Any(value => value.Target.Request == item.Retained.Message.Request))
                || represented != lifecycleSubmissions.Count)
            { throw new InvalidOperationException("A submission is still being admitted or completing. Wait for its exact acceptance/completion and review again."); }
            var work = entries.Select(item => new ApplicationLifecycleWork(item.Target, item.Retained!.Message.Request,
                running.FirstOrDefault(value => value.Target.Request == item.Retained.Message.Request)?.Phase, item.Phase,
                item.Retained.Message.Body))
                .Concat(running.Where(item => !entries.Any(entry => entry.Retained!.Message.Request == item.Target.Request))
                    .Select(item => new ApplicationLifecycleWork(lifecycleSubmissions.Single(entry =>
                        entry.Target.Authority.SessionId == item.Target.Request.SessionId).Target,
                        item.Target.Request, item.Phase, null, lifecycleSubmissions.Single(entry =>
                            entry.Target.Authority.SessionId == item.Target.Request.SessionId).Description))).ToImmutableArray();
            lifecycleHeld = true;
            review = lifecycleReview = new(operation, origin, control, lifecycleRevision, expires, SourceCurrent,
                work, running, pending, ReleaseApplicationLifecycle);
        }
        try
        {
            await ValidateLifecycleSourcesAsync(review, token).ConfigureAwait(false);
            lock (cancellationGate) { RequireLifecycleCurrent(review, exactWork: true); }
            return review;
        }
        catch
        {
            review.Dispose();
            throw;
        }
    }

    public bool IsApplicationLifecycleCurrent(ApplicationLifecycleReview review)
    {
        lock (cancellationGate)
        {
            return ReferenceEquals(lifecycleReview, review) && lifecycleHeld && !review.ReleasePending && review.SourceCurrent()
                && !pendingClosed && !lifecycleUnconfirmed;
        }
    }

    private void RequireLifecycleCurrent(ApplicationLifecycleReview review, bool exactWork)
    {
        if (!IsApplicationLifecycleCurrent(review) || exactWork && (review.Consumed
            || lifecycleRevision != review.WorkRevision
            || !review.Running.SequenceEqual(runningConversations.Values.Select(item => item.Status))
            || review.Pending.Any(expected => Snapshot(expected.Session, conversationLanes[expected.Session]).Revision != expected.Revision)))
        { throw new InvalidOperationException("The exact submitted work, original channel or private authority changed. Review again; no new stop was authorized."); }
    }

    private async Task ValidateLifecycleSourcesAsync(ApplicationLifecycleReview review, CancellationToken token)
    {
        foreach (var target in review.Work.Select(item => item.Session).Distinct())
        {
            var actual = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            if (actual != target || !actual.Authority.IsActive)
            { throw new InvalidOperationException("A reviewed session's generation or ownership changed."); }
            lock (cancellationGate) { RequireLifecycleCurrent(review, exactWork: true); }
        }
        foreach (var work in review.Work)
        {
            var revision = review.Running.FirstOrDefault(item => item.Target.Request == work.Request)?.Target.TaskRevision
                ?? review.Pending.SelectMany(item => item.Turns)
                    .Single(item => item.Submission.Message.Request == work.Request).Submission.Task.Revision;
            var actual = await store.ReadTaskAsync(work.Request.SessionId, work.Request.TaskId, token).ConfigureAwait(false);
            if (actual is null || !actual.CurrentSource || actual.Generation != work.Session.Authority.Generation
                || actual.Task.Request != work.Request || actual.Task.Revision != revision)
            { throw new InvalidOperationException("The exact reviewed submitted task changed. No stop was accepted."); }
            lock (cancellationGate) { RequireLifecycleCurrent(review, exactWork: true); }
        }
        token.ThrowIfCancellationRequested();
    }

    public async Task ConfirmApplicationLifecycleAsync(ApplicationLifecycleReview review, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        RequireOriginalSource(review.Origin);
        await ValidateLifecycleSourcesAsync(review, token).ConfigureAwait(false);
        ConversationCancellationLease[] running = [];
        PendingConversation[] pending = [];
        LifecycleSubmission[] submissions = [];
        var signals = new List<Task>();
        Exception? stopFailure = null;
        try
        {
            lock (cancellationGate)
            {
                RequireLifecycleCurrent(review, exactWork: true);
                // Every audit and exact-set check precedes any continuation withdrawal.
                foreach (var work in review.Work)
                {
                    using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), work.Request.SessionId,
                        work.Request.TaskId, review.Origin), HostActivityLayer.Application, HostOperation.Request);
                    AuditPendingControl("application.lifecycle-stop", () => IsApplicationLifecycleCurrent(review), review.Origin);
                    RequireLifecycleCurrent(review, exactWork: true);
                    activity.Complete(HostOperationOutcome.Completed);
                }
                running = runningConversations.Values.ToArray();
                pending = conversationLanes.Values.SelectMany(item => item.Entries).ToArray();
                submissions = lifecycleSubmissions.ToArray();
                review.Consumed = true;
                review.Joining = true;
                foreach (var entry in pending.Where(item => item.Phase == ConversationPendingPhase.Pending))
                {
                    entry.CancelRequested = true;
                    var lane = conversationLanes[entry.Target.Authority.SessionId];
                    lane.Paused = true;
                    lane.Pulse();
                }
                foreach (var lease in running)
                {
                    if (lease.Status.Phase is not (ConversationExecutionPhase.Waiting or ConversationExecutionPhase.Running)) { continue; }
                    signals.Add(lease.RequestCancellationAsync(() =>
                    {
                        if (!IsApplicationLifecycleCurrent(review))
                        { throw new InvalidOperationException("Original lifecycle authority was revoked before exact stop acceptance."); }
                    }));
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            stopFailure = exception;
        }
        if (review.Consumed)
        {
            try
            {
#pragma warning disable VSTHRD003 // These captured host-owned tasks finish off the UI context and must be physically joined.
                await Task.WhenAll(signals.Concat(running.Select(item => item.JoinPhysicalCompletionAsync()))
                    .Concat(submissions.Select(item => item.Completed.Task))
                    .Concat(pending.Select(item => item.Worker))).ConfigureAwait(false);
                var completed = await Task.WhenAll(pending.Select(item => item.Completed.Task)).ConfigureAwait(false);
#pragma warning restore VSTHRD003
                if (running.Any(item => item.Status.Phase != ConversationExecutionPhase.Terminal)
                    || completed.Any(item => item.Failure is not null || item.Receipt is null))
                { throw new InvalidOperationException("Submitted work release or terminal retention is unconfirmed. No application effect is permitted."); }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lock (cancellationGate) { lifecycleUnconfirmed = true; }
                if (stopFailure is not null) { throw new AggregateException(stopFailure, exception); }
                throw;
            }
            finally
            {
                lock (cancellationGate)
                {
                    review.Joining = false;
                    if (review.ReleasePending) { ReleaseApplicationLifecycle(review); }
                }
            }
        }
        if (stopFailure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stopFailure).Throw(); }
        foreach (var target in review.Work.Select(item => item.Session).Distinct())
        {
            if (await store.ReadMetadataAsync(target.Authority.SessionId, CancellationToken.None).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("Reviewed session ownership changed during resource joins. No application effect is permitted."); }
        }
        lock (cancellationGate)
        {
            RequireLifecycleCurrent(review, exactWork: false);
            review.Joined = true;
        }
    }

    private void ReleaseApplicationLifecycle(ApplicationLifecycleReview review)
    {
        lock (cancellationGate)
        {
            if (!ReferenceEquals(lifecycleReview, review)) { return; }
            if (review.Joining) { review.ReleasePending = true; return; }
            lifecycleReview = null;
            lifecycleHeld = false;
            foreach (var lane in conversationLanes.Values) { lane.Pulse(); }
        }
    }
}
