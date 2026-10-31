using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public async Task<SessionRemainingWorkAnswer> ReadRemainingWorkAsync(SessionWorkspaceEntry target,
        Func<bool> admitted, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        token = cancellation.Token;
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.ControlRevision == control && admitted();
        RequireRemainingWorkAdmission(Live, token);
        var before = await workspace.ReadWorkAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        RequireRemainingWorkSubject(before, target);
        ConversationWorkObservation ordinary;
        DateTimeOffset observedAt;
        lock (cancellationGate)
        {
            RequireRemainingWorkAdmission(Live, token);
            ordinary = ObserveRemainingWork(target, control);
            observedAt = time.GetUtcNow();
        }
        var durable = await workspace.ReadWorkAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        RequireRemainingWorkSubject(durable, target);
        RequireRemainingWorkAdmission(Live, token);
        var records = ProjectRemainingWorkTasks(durable);
        var answer = new SessionRemainingWorkAnswer(target, durable.ObservedAt, observedAt,
            [.. records.Where(task => task.State is HostTaskState.IntentRecorded or HostTaskState.DispatchRecorded
                or HostTaskState.Unknown or HostTaskState.Interrupted)],
            durable.PendingQuestions, durable.OmittedTasks, durable.OmittedQuestions,
            durable.QueueRecords.Length, durable.OmittedQueueRecords)
        {
            AuthorityRevision = durable.AuthorityRevision,
            SourceTasks = records,
            Ordinary = ordinary,
            CurrentReceipt = records.FirstOrDefault(task => task.Request == ordinary.Running?.Target.Request),
            ExpiresAt = observedAt.AddSeconds(10)
        };
        if (!MatchesRemainingWorkSource(answer, before) || !IsRemainingWorkCurrent(answer, Live))
        { throw RemainingWorkChanged(); }
        if (ordinary.Running is { } running && (answer.CurrentReceipt is { } receipt
                ? receipt.Generation != running.Target.Generation || receipt.Revision != running.Target.TaskRevision
                : durable.OmittedTasks == 0))
        { throw RemainingWorkChanged(); }
        return answer;
    }

    public async Task ValidateRemainingWorkAsync(SessionRemainingWorkAnswer answer, Func<bool> admitted, CancellationToken token)
    {
        RequireRemainingWorkAdmission(admitted, token);
        var current = await workspace.ReadWorkAsync(answer.Session.Authority.SessionId, token).ConfigureAwait(false);
        RequireRemainingWorkSubject(current, answer.Session);
        RequireRemainingWorkAdmission(admitted, token);
        if (!MatchesRemainingWorkSource(answer, current) || !IsRemainingWorkCurrent(answer, admitted))
        { throw RemainingWorkChanged(); }
    }

    private static System.Collections.Immutable.ImmutableArray<RemainingWorkTask> ProjectRemainingWorkTasks(SessionWorkSnapshot snapshot) =>
        [.. snapshot.Tasks.Select(task => new RemainingWorkTask(task.Task.Request, task.Generation,
            task.Task.Revision, task.Task.State, task.CurrentSource))];

    private static bool MatchesRemainingWorkSource(SessionRemainingWorkAnswer answer, SessionWorkSnapshot snapshot) =>
        snapshot.AuthorityRevision == answer.AuthorityRevision && snapshot.Session == answer.Session
        && ProjectRemainingWorkTasks(snapshot).SequenceEqual(answer.SourceTasks)
        && snapshot.PendingQuestions.SequenceEqual(answer.Questions) && snapshot.OmittedTasks == answer.OmittedTasks
        && snapshot.OmittedQuestions == answer.OmittedQuestions && snapshot.QueueRecords.Length == answer.OtherQueueRecords
        && snapshot.OmittedQueueRecords == answer.OmittedQueueRecords;

    public bool IsRemainingWorkCurrent(SessionRemainingWorkAnswer answer, Func<bool> admitted)
    {
        lock (cancellationGate)
        {
            var now = time.GetUtcNow();
            if (!access.CanInspect || access.ControlRevision != answer.Ordinary.ControlRevision || !admitted()
                || pendingClosed || now < answer.OrdinaryObservedAt || now >= answer.ExpiresAt
                || answer.Ordinary.Turns.Any(turn => turn.ExpiresAt > answer.OrdinaryObservedAt && turn.ExpiresAt <= now)
                || answer.Questions.Any(question => question.ExpiresAt > answer.DurableObservedAt && question.ExpiresAt <= now))
            { return false; }
            var current = ObserveRemainingWork(answer.Session, answer.Ordinary.ControlRevision);
            return current with { Turns = answer.Ordinary.Turns } == answer.Ordinary
                && current.Turns.SequenceEqual(answer.Ordinary.Turns);
        }
    }

    private ConversationWorkObservation ObserveRemainingWork(SessionWorkspaceEntry target, long control)
    {
        ObjectDisposedException.ThrowIf(pendingClosed, this);
        conversationLanes.TryGetValue(target.Authority.SessionId, out var lane);
        runningConversations.TryGetValue(target.Authority.SessionId, out var lease);
        var running = lease?.Status;
        if (running is not null && running.Target.Generation != target.Authority.Generation)
        { throw RemainingWorkChanged(); }
        return new(control, lane?.Revision ?? 0, lifecycleRevision, lane?.Paused ?? false, lifecycleHeld,
            lane?.Entries.Count(entry => entry.Retained is null) ?? 0,
            lane is null ? [] : [.. Snapshot(target.Authority.SessionId, lane).Turns.Select(turn =>
                new RemainingWorkTurn(turn.Submission.Message.Request, turn.Submission.Message.Generation,
                    turn.Submission.Task.Revision, turn.ExpiresAt, turn.Phase, turn.Position))],
            running, running is not null && IsExecutionProgressCurrent(running))
        {
            ManualHoldReason = lane?.ManualHoldReason ?? ConversationDispatchHoldReason.None,
        };
    }

    private static void RequireRemainingWorkSubject(SessionWorkSnapshot snapshot, SessionWorkspaceEntry target)
    {
        snapshot.RequireSubject(target.Authority.SessionId);
        if (snapshot.Session != target) { throw RemainingWorkChanged(); }
        if (snapshot.Tasks.IsDefault || snapshot.PendingQuestions.IsDefault || snapshot.QueueRecords.IsDefault
            || snapshot.Tasks.Length > SessionWorkSnapshot.MaximumRecords || snapshot.PendingQuestions.Length > SessionWorkSnapshot.MaximumRecords
            || snapshot.OmittedTasks < 0 || snapshot.OmittedQuestions < 0 || snapshot.OmittedQueueRecords < 0
            || snapshot.Tasks.Any(task => task.Task.Request.SessionId != target.Authority.SessionId)
            || snapshot.PendingQuestions.Any(question => question.Key.Request.SessionId != target.Authority.SessionId))
        { throw new InvalidDataException("The remaining-work ledger is incomplete or belongs to another conversation."); }
    }

    private static void RequireRemainingWorkAdmission(Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Remaining-work inspection requires unchanged original private admission."); }
    }

    private static InvalidOperationException RemainingWorkChanged() =>
        new("Remaining-work sources changed during observation. Ask again; no empty/completed outcome or automatic retry is inferred.");
}