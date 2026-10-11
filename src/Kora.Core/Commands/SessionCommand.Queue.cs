using System.Text;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public long QueueRevision { get; init; }
    public Guid? WorkRequestId { get; init; }
    public Guid? DependencyTaskId { get; init; }
    public Guid? QueueRunId { get; init; }
    public long QueueControlRevision { get; init; }
    public Guid? QueueNeighborId { get; init; }
    public long QueueNeighborRevision { get; init; }
    public Guid? QueueMoveReviewId { get; init; }
    public SessionQueueReadProfile QueueProfile { get; init; } = SessionQueueReadProfile.Version;
    public const string QueueSyntax =
        "queue help | queue list <session-id> | queue status <session-id> <task-id> | "
        + "queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> version|capabilities.list|readiness.get|runtime.list [after <task-id>] | "
        + "queue cancel|remove <session-id> <generation> <queue-revision> <task-id> <entry-revision> | "
        + "queue clear <session-id> <generation> <queue-revision> confirm | "
        + "queue dispatch <session-id> <generation> <queue-revision>. "
        + "queue move-up|move-down <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id> <entry-revision> <neighbor-id> <neighbor-revision> | "
        + "queue move-confirm <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id> <entry-revision> <neighbor-id> <neighbor-revision> <review-id> confirm. "
        + "queue move-cancel <session-id> <review-id>. "
        + "Move requires an exact observed adjacent-pair review and separate confirmation; no automatic dispatch or clock renewal. "
        + "queue pause-status <session-id> | queue pause|resume <session-id> <generation> <queue-revision> <run-id> <pause-revision>. "
        + "Pause blocks new admissions only for that exact current-run session; resume never dispatches or replays work. "
        + "Four fixed no-argument first-page reads only; manual fair dispatch of already enqueued current-run work. "
        + "Queue revision may be zero for an empty queue. No effects, workers, providers, model or restart replay.";

    private static SessionCommand ParseQueue(string text, string input)
    {
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: QueueSyntax);
        if (Encoding.UTF8.GetByteCount(input) > MaximumInputBytes || input.Any(char.IsControl)) { return Invalid(); }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && words[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return new(SessionCommandOperation.Help);
        }
        if (words.Length < 3 || !ExactId(words[2], out var session)) { return Invalid(); }
        var operation = words[1].ToLowerInvariant();
        if (operation is "move-cancel" && words.Length == 4 && ExactId(words[3], out var cancelledReview))
        {
            return new(SessionCommandOperation.QueueMoveCancel, session) { QueueMoveReviewId = cancelledReview };
        }
        if (operation is "list" && words.Length == 3) { return new(SessionCommandOperation.QueueList, session); }
        if (operation is "pause-status" && words.Length == 3) { return new(SessionCommandOperation.QueuePauseStatus, session); }
        if (operation is "status" && words.Length == 4 && ExactId(words[3], out var statusTask))
        {
            return new(SessionCommandOperation.QueueStatus, session) { TaskId = statusTask };
        }
        if (words.Length < 5 || !Revision(words[3], true, out var generation)
            || !Revision(words[4], false, out var revision)) { return Invalid(); }
        if (operation is "dispatch" && words.Length == 5)
        {
            return new(SessionCommandOperation.QueueDispatch, session, generation) { QueueRevision = revision };
        }
        if (operation is "move-up" or "move-down" or "move-confirm"
            && words.Length == (operation is "move-confirm" ? 13 : 11)
            && ExactId(words[5], out var moveRun) && Revision(words[6], false, out var moveControl)
            && ExactId(words[7], out var moveTask) && Revision(words[8], true, out var moveRevision)
            && ExactId(words[9], out var neighbor) && neighbor != moveTask
            && Revision(words[10], true, out var neighborRevision))
        {
            Guid? review = null;
            if (operation is "move-confirm")
            {
                if (!ExactId(words[11], out var reviewId) || !words[12].Equals("confirm", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
                review = reviewId;
            }
            return new(operation switch
            {
                "move-up" => SessionCommandOperation.QueueMoveUp,
                "move-down" => SessionCommandOperation.QueueMoveDown,
                _ => SessionCommandOperation.QueueMoveConfirm,
            }, session, generation)
            {
                QueueRevision = revision, QueueRunId = moveRun, QueueControlRevision = moveControl,
                TaskId = moveTask, TaskRevision = moveRevision, QueueNeighborId = neighbor,
                QueueNeighborRevision = neighborRevision, QueueMoveReviewId = review,
            };
        }
        if (operation is "pause" or "resume" && words.Length == 7 && ExactId(words[5], out var run)
            && Revision(words[6], false, out var controlRevision))
        {
            return new(operation is "pause" ? SessionCommandOperation.QueuePause : SessionCommandOperation.QueueResume,
                session, generation) { QueueRevision = revision, QueueRunId = run, QueueControlRevision = controlRevision };
        }
        if (operation is "clear" && words.Length == 6 && words[5].Equals("confirm", StringComparison.OrdinalIgnoreCase))
        {
            return new(SessionCommandOperation.QueueClear, session, generation) { QueueRevision = revision };
        }
        if (operation is "cancel" or "remove" && words.Length == 7 && ExactId(words[5], out var task)
            && Revision(words[6], true, out var entryRevision))
        {
            return new(operation is "cancel" ? SessionCommandOperation.QueueCancel : SessionCommandOperation.QueueRemove,
                session, generation) { QueueRevision = revision, TaskId = task, TaskRevision = entryRevision };
        }
        if (operation is "enqueue" && words.Length is 8 or 10 && ExactId(words[5], out var request)
            && ExactId(words[6], out var queuedTask) && SessionQueueReadProfiles.Parse(words[7]) is { } profile)
        {
            Guid? dependency = null;
            if (words.Length == 10)
            {
                if (!words[8].Equals("after", StringComparison.OrdinalIgnoreCase) || !ExactId(words[9], out var after)) { return Invalid(); }
                dependency = after;
            }
            return new(SessionCommandOperation.QueueEnqueue, session, generation)
            { QueueRevision = revision, WorkRequestId = request, TaskId = queuedTask, DependencyTaskId = dependency, QueueProfile = profile };
        }
        return Invalid();
    }
}
