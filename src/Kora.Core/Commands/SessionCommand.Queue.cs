using System.Text;
using System.Collections.Immutable;
using System.Globalization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public long QueueRevision { get; init; }
    public Guid? WorkRequestId { get; init; }
    public Guid? DependencyTaskId { get; init; }
    public HostRevision? ExpectedDependencyRevision { get; init; }
    public Guid? QueueRunId { get; init; }
    public long QueueControlRevision { get; init; }
    public Guid? QueueNeighborId { get; init; }
    public long QueueNeighborRevision { get; init; }
    public Guid? QueueMoveReviewId { get; init; }
    public Guid? QueueCancelReviewId { get; init; }
    public Guid? QueueRequeueReviewId { get; init; }
    public Guid? FreshRequestId { get; init; }
    public Guid? FreshTaskId { get; init; }
    public ImmutableArray<SessionQueueSelection> QueueSelection { get; init; } = [];
    public SessionQueueReadProfile QueueProfile { get; init; } = SessionQueueReadProfile.Version;
    public SessionQueueCapabilityTarget? QueueTarget { get; init; }
    public const string QueueSyntax =
        "queue help | queue list <session-id> | queue status <session-id> <task-id> | "
        + "queue cancel|remove <session-id> <generation> <queue-revision> <task-id> <entry-revision> | "
        + "queue clear <session-id> <generation> <queue-revision> confirm | "
        + "queue cancel-selected-review <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id>:<entry-revision>[,<task-id>:<entry-revision>...] | "
        + "queue cancel-selected-confirm <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id>:<entry-revision>[,...] <review-id> confirm | "
        + "queue cancel-selected-discard <session-id> <review-id>. "
        + "Review one to ten exact pending IDs in displayed dispatch order; confirm cancels these pending entries only, atomically, retaining history and dependents. "
        + "queue pause-status <session-id>. "
        + "Existing records only. Diagnostic-only enqueue, dispatch, reorder and expired replacement are retired. "
        + "Queue revision may be zero for an empty queue. No admission, deadline rewriting, effects or restart replay.";

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
        if (operation is "cancel-selected-discard" && words.Length == 4
            && CanonicalQueueId(words[2], out _) && CanonicalQueueId(words[3], out var discardedReview))
        {
            return new(SessionCommandOperation.QueueCancelSelectedDiscard, session) { QueueCancelReviewId = discardedReview };
        }
        if (operation is "list" && words.Length == 3) { return new(SessionCommandOperation.QueueList, session); }
        if (operation is "pause-status" && words.Length == 3) { return new(SessionCommandOperation.QueuePauseStatus, session); }
        if (operation is "status" && words.Length == 4 && ExactId(words[3], out var statusTask))
        {
            return new(SessionCommandOperation.QueueStatus, session) { TaskId = statusTask };
        }
        if (words.Length < 5 || !Revision(words[3], true, out var generation)
            || !Revision(words[4], false, out var revision)) { return Invalid(); }
        if (operation is "cancel-selected-review" or "cancel-selected-confirm")
        {
            var confirm = operation is "cancel-selected-confirm";
            if (words.Length != (confirm ? 10 : 8) || !CanonicalQueueId(words[2], out _)
                || !CanonicalQueueRevision(words[3], true, out _) || !CanonicalQueueRevision(words[4], false, out _)
                || !CanonicalQueueId(words[5], out var selectedRun)
                || !CanonicalQueueRevision(words[6], false, out var selectedControl)) { return Invalid(); }
            var members = words[7].Split(',');
            if (members.Length > SessionQueueLimits.MaximumPendingPerSession) { return Invalid(); }
            var selection = ImmutableArray.CreateBuilder<SessionQueueSelection>(members.Length);
            foreach (var member in members)
            {
                var pair = member.Split(':');
                if (pair.Length != 2 || !CanonicalQueueId(pair[0], out var memberId) || !CanonicalQueueRevision(pair[1], true, out var memberRevision)
                    || selection.Any(selected => selected.TaskId.Value == memberId)) { return Invalid(); }
                selection.Add(new(new(memberId), new(memberRevision)));
            }
            Guid? selectedReview = null;
            if (confirm)
            {
                if (!CanonicalQueueId(words[8], out var confirmedReview) || !words[9].Equals("confirm", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
                selectedReview = confirmedReview;
            }
            return new(confirm ? SessionCommandOperation.QueueCancelSelectedConfirm : SessionCommandOperation.QueueCancelSelectedReview,
                session, generation)
            {
                QueueRevision = revision, QueueRunId = selectedRun, QueueControlRevision = selectedControl,
                QueueSelection = selection.MoveToImmutable(), QueueCancelReviewId = selectedReview,
            };
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
        return Invalid();
    }

    private static bool CanonicalQueueId(string text, out Guid id) =>
        ExactId(text, out id) && string.Equals(text, id.ToString("D"), StringComparison.Ordinal);

    private static bool CanonicalQueueRevision(string text, bool positive, out long revision) =>
        Revision(text, positive, out revision)
        && string.Equals(text, revision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
