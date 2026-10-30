using System.Diagnostics;

using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private sealed class ConversationLane(long controlRevision) : IDisposable
    {
        internal readonly SemaphoreSlim Admission = new(1, 1);
        internal readonly List<PendingConversation> Entries = [];
        internal TaskCompletionSource Changed = NewSignal();
        internal long Revision;
        internal long ControlRevision = controlRevision;
        internal bool Paused;
        internal bool PendingClearHeld;
        public void Dispose() => Admission.Dispose();
        internal void Pulse()
        {
            Revision++;
            var previous = Changed;
            Changed = NewSignal();
            previous.TrySetResult();
        }
    }

    internal sealed class PendingConversation(SessionWorkspaceEntry target, IReadOnlyList<HostRequest> predecessors, Action? accepted,
        RetryIntent? retry)
    {
        internal RetryIntent? Retry { get; } = retry;
        internal SessionWorkspaceEntry Target { get; } = target;
        internal IReadOnlyList<HostRequest> Predecessors { get; } = predecessors;
        internal readonly TaskCompletionSource<ConversationQueuedSubmission> Accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<ConversationTurnCompletion> Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConversationSubmissionReceipt? Retained;
        internal DateTimeOffset ExpiresAt;
        internal ConversationPendingPhase Phase;
        internal bool CancelRequested;
        internal Func<bool>? PendingCancellationCurrent;
        internal SessionWorkspaceEntry? PendingCancellationTarget;
        internal ActivityLink Cause;
        internal Action? OnAccepted { get; } = accepted;
        internal Task Worker = Task.CompletedTask;
    }

    private readonly Dictionary<HostId<SessionIdentity>, ConversationLane> conversationLanes = [];
    private readonly CancellationTokenSource pendingLifetime = new();
    private readonly List<Task> pendingWorkers = [];
    private bool pendingClosed;
    private Task? pendingDisposal;
    public event Action<ConversationPendingSnapshot>? PendingConversationChanged;
    public event Action<SessionWorkspaceEntry, ConversationTurnCompletion>? SubmittedConversationCompleted;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "The host-owned worker signals acceptance off the UI context; all continuations are asynchronous.")]
    public Task<ConversationQueuedSubmission> SubmitQueuedAsync(SessionWorkspaceEntry target, string body,
        Func<bool> admitted, CancellationToken token, Func<CancellationToken, Task>? validateSource = null,
        IEnumerable<ActivityLink>? links = null, RequestOrigin origin = RequestOrigin.LocalUi, bool localAnswer = false,
        Action? accepted = null) =>
        SubmitQueuedCoreAsync(target, body, admitted, validateSource, links, origin, localAnswer, accepted, null, token);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "The host-owned worker signals acceptance off the UI context; all continuations are asynchronous.")]
    private async Task<ConversationQueuedSubmission> SubmitQueuedCoreAsync(SessionWorkspaceEntry target, string body,
        Func<bool> admitted, Func<CancellationToken, Task>? validateSource,
        IEnumerable<ActivityLink>? links, RequestOrigin origin, bool localAnswer, Action? accepted, RetryIntent? retry,
        CancellationToken token)
    {
        RequireOriginalSource(origin);
        ConversationLane lane;
        lock (cancellationGate)
        {
            ObjectDisposedException.ThrowIf(pendingClosed, this);
            RequireConversationDispatchOpen(target.Authority.SessionId);
            if (!conversationLanes.TryGetValue(target.Authority.SessionId, out lane!))
            { conversationLanes.Add(target.Authority.SessionId, lane = new(access.ControlRevision)); }
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, pendingLifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (!await lane.Admission.WaitAsync(0, linked.Token).ConfigureAwait(false))
        { throw new InvalidOperationException("This conversation is already accepting a submitted request. Wait for its acceptance before submitting another."); }
        try
        {
            PendingConversation pending;
            lock (cancellationGate)
            {
                ObjectDisposedException.ThrowIf(pendingClosed, this);
                RequireConversationDispatchOpen(target.Authority.SessionId);
                if (lane.Entries.Count == 0) { lane.ControlRevision = access.ControlRevision; }
                if (lane.Entries.Count(item => item.Phase == ConversationPendingPhase.Pending) >= SessionQueueLimits.MaximumPendingPerSession)
                { throw new InvalidOperationException("The conversation queue is full. Remove or defer pending work; nothing was evicted."); }
                pending = new(target, lane.Entries.Where(item => item.Retained is not null)
                    .Select(item => item.Retained!.Message.Request).ToArray(), accepted, retry);
                lane.Entries.Add(pending);
                // The worker owns the original source token, issuer and terminal join, not the acceptance caller.
                pending.Worker = RunPendingConversationAsync(lane, pending, body, admitted,
                    validateSource, links?.ToArray(), origin, localAnswer, token);
                pendingWorkers.RemoveAll(item => item.IsCompletedSuccessfully);
                pendingWorkers.Add(pending.Worker);
            }
            return await pending.Accepted.Task.ConfigureAwait(false);
        }
        finally { lane.Admission.Release(); }
    }

    private async Task<ConversationSubmissionReceipt> SubmitOrdinaryQueuedAndJoinAsync(SessionWorkspaceEntry target,
        string body, Func<bool> admitted, Func<CancellationToken, Task>? validateSource,
        IEnumerable<ActivityLink>? links, RequestOrigin origin, CancellationToken token, bool localAnswer = false,
        Action? accepted = null)
    {
        var submitted = await SubmitQueuedAsync(target, body, admitted, token, validateSource, links, origin, localAnswer, accepted).ConfigureAwait(false);
        if (submitted.Position > 0) { return submitted.Receipt; }
        try { return (await submitted.Completion.WaitAsync(token).ConfigureAwait(false)).RequireReceipt(); }
        catch (OperationCanceledException exception) when (token.IsCancellationRequested)
        { throw new ConversationSubmissionUnconfirmedException(submitted.Receipt.Message.Request, exception); }
    }

    private async Task RunPendingConversationAsync(ConversationLane lane, PendingConversation pending,
        string body, Func<bool> admitted, Func<CancellationToken, Task>? validateSource,
        ActivityLink[]? links, RequestOrigin origin, bool localAnswer, CancellationToken token)
    {
        ConversationTurnCompletion completion;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, pendingLifetime.Token);
        try
        {
            var receipt = await SubmitOrdinaryWithScopeAsync(pending.Target, body, admitted,
                validateSource, links, origin, localAnswer, pending, linked.Token).ConfigureAwait(false);
            completion = new(receipt, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            completion = new(null, exception);
        }
        lock (cancellationGate)
        {
            lane.Entries.Remove(pending);
            lane.Paused |= pending.Retained is not null && completion.Receipt?.Message.Outcome != ModelTurnOutcome.Succeeded;
            lane.Pulse();
            pending.Completed.TrySetResult(completion);
            if (!pending.Accepted.Task.IsCompleted)
            {
                if (completion.Failure is { } failure) { pending.Accepted.TrySetException(failure); }
                else { pending.Accepted.TrySetResult(new(completion.Receipt!, 0, pending.Completed.Task)); }
            }
            PendingConversationChanged?.Invoke(Snapshot(pending.Target.Authority.SessionId, lane));
        }
        if (completion.Receipt is not null || completion.Failure is ConversationSubmissionUnconfirmedException)
        { SubmittedConversationCompleted?.Invoke(pending.Target, completion); }
    }

    private async Task<ModelTurnReason> AwaitPendingDispatchAsync(PendingConversation pending,
        ConversationSubmissionReceipt retained, ModelContextEnvelope context, Func<bool> live, CancellationToken token)
    {
        ConversationLane lane;
        lock (cancellationGate)
        {
            if (!live()) { throw new InvalidOperationException("Original submitted source authority changed before pending acceptance."); }
            lane = conversationLanes[pending.Target.Authority.SessionId];
            pending.Retained = retained;
            pending.ExpiresAt = context.ExpiresAt;
            pending.Cause = new(HostActivity.RequireCurrent().Activity!.Context);
            pending.OnAccepted?.Invoke();
            if (!lifecycleHeld && !lane.Paused && !lane.PendingClearHeld && ReferenceEquals(lane.Entries[0], pending))
            { pending.Phase = ConversationPendingPhase.Dispatching; }
            lane.Pulse();
            var snapshot = Snapshot(pending.Target.Authority.SessionId, lane);
            pending.Accepted.TrySetResult(new(retained, snapshot.Turns.Single(item => item.Submission == retained).Position, pending.Completed.Task));
            PendingConversationChanged?.Invoke(Snapshot(pending.Target.Authority.SessionId, lane));
        }
        while (true)
        {
            Task changed;
            TimeSpan remaining;
            lock (cancellationGate)
            {
                ExpireLifecycleBarrier();
                if (pending.CancelRequested) { return ModelTurnReason.CallerCancelled; }
                remaining = pending.ExpiresAt - time.GetUtcNow();
                if (remaining <= TimeSpan.Zero || time.GetUtcNow() < context.CreatedAt) { return ModelTurnReason.ContextExpired; }
                token.ThrowIfCancellationRequested();
                if (!lifecycleHeld && !lane.Paused && !lane.PendingClearHeld && ReferenceEquals(lane.Entries[0], pending))
                {
                    pending.Phase = ConversationPendingPhase.Dispatching;
                    lane.Pulse();
                    PendingConversationChanged?.Invoke(Snapshot(pending.Target.Authority.SessionId, lane));
                    return ModelTurnReason.None;
                }
                changed = lane.Changed.Task;
            }
            try { await changed.WaitAsync(remaining, time, token).ConfigureAwait(false); }
            catch (TimeoutException) { return ModelTurnReason.ContextExpired; }
        }
    }

    private static ConversationPendingSnapshot Snapshot(HostId<SessionIdentity> session, ConversationLane lane)
    {
        var position = 0;
        return new(session, lane.Revision, lane.Paused,
            [.. lane.Entries.Where(item => item.Retained is not null).Select(item => new ConversationPendingTurn(
                item.Retained!, item.ExpiresAt, item.Phase,
                item.Phase == ConversationPendingPhase.Dispatching ? 0 : ++position))])
        { ControlRevision = lane.ControlRevision };
    }

    public async Task<ConversationPendingSnapshot> ReadPendingConversationAsync(SessionWorkspaceEntry target,
        Func<bool> admitted, CancellationToken token)
    {
        var revision = access.ControlRevision;
        bool Live() => access.CanInspect && access.ControlRevision == revision && admitted();
        var actual = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Live() || actual != target) { throw new InvalidOperationException("Refresh the exact private conversation before reading its submitted work."); }
        lock (cancellationGate)
        {
            ObjectDisposedException.ThrowIf(pendingClosed, this);
            return (conversationLanes.TryGetValue(target.Authority.SessionId, out var lane)
                ? Snapshot(target.Authority.SessionId, lane) : new(target.Authority.SessionId, 0, false, []))
                with { ControlRevision = revision };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Joins only the addressed host-owned pending worker off the UI context, never an active provider.")]
    public async Task<ConversationSubmissionReceipt> CancelPendingConversationAsync(SessionWorkspaceEntry target,
        ConversationPendingTurn expected, long revision, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        RequireOriginalSource(origin);
        var control = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == control && admitted();
        var actual = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        PendingConversation pending;
        lock (cancellationGate)
        {
            RequireConversationDispatchOpen(target.Authority.SessionId);
            if (!Live() || actual != target || !target.Authority.IsActive
                || !conversationLanes.TryGetValue(target.Authority.SessionId, out var lane)
                || lane.Revision != revision
                || !Snapshot(target.Authority.SessionId, lane).Turns.Contains(expected)
                || expected.Phase != ConversationPendingPhase.Pending)
            { throw new InvalidOperationException("The exact pending request changed. Refresh before cancelling; active work has its separate stop control."); }
            pending = lane.Entries.Single(item => item.Retained == expected.Submission);
            using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
                expected.Submission.Task.Request.TaskId, origin), HostActivityLayer.Application, HostOperation.Request,
                [pending.Cause]);
            AuditPendingControl("conversation.pending-cancel", Live, origin);
            pending.CancelRequested = true;
            lane.Paused = true;
            lane.Pulse();
            activity.Complete(HostOperationOutcome.Completed);
        }
        return (await pending.Completed.Task.ConfigureAwait(false)).RequireReceipt();
    }

    public async Task ResumePendingConversationAsync(SessionWorkspaceEntry target, long revision,
        Func<bool> admitted, CancellationToken token, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        RequireOriginalSource(origin);
        var control = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == control && admitted();
        var actual = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (cancellationGate)
        {
            RequireConversationDispatchOpen(target.Authority.SessionId);
            if (!Live() || actual != target || !target.Authority.IsActive
                || !conversationLanes.TryGetValue(target.Authority.SessionId, out var lane)
                || lane.Revision != revision || !lane.Paused)
            { throw new InvalidOperationException("Refresh the exact held queue before explicitly resuming. No authority was renewed."); }
            using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
                new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request);
            AuditPendingControl("conversation.pending-resume", Live, origin);
            lane.Paused = false;
            lane.Pulse();
            PendingConversationChanged?.Invoke(Snapshot(target.Authority.SessionId, lane));
            activity.Complete(HostOperationOutcome.Completed);
        }
    }

    private void AuditPendingControl(string action, Func<bool> live, RequestOrigin origin)
    {
        var audit = cancellationAudit ?? throw new InvalidOperationException("Trusted work-control audit is unavailable.");
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            action, SecurityAuditOutcome.Requested,
            origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            HostActivity.RequireCurrent().Request.TaskId.Value.ToString("D"));
        audit.Write(requested);
        if (!live()) { audit.Write(requested.WithOutcome(SecurityAuditOutcome.Denied)); throw new InvalidOperationException("Original work-control authority changed during audit."); }
        audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
        if (!live()) { throw new InvalidOperationException("Original work-control authority changed after audit."); }
    }

    public ValueTask DisposeAsync()
    {
        lock (cancellationGate) { return new(pendingDisposal ??= DisposePendingAsync()); }
    }

    private async Task DisposePendingAsync()
    {
        Task[] workers;
        lock (cancellationGate)
        {
            pendingClosed = true;
            workers = pendingWorkers.ToArray();
        }
        await pendingLifetime.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(workers).ConfigureAwait(false);
        foreach (var lane in conversationLanes.Values)
        {
            await lane.Admission.WaitAsync().ConfigureAwait(false);
            lane.Dispose();
        }
        pendingLifetime.Dispose();
    }
}
