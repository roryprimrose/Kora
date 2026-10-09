using System.Text;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public long QueueRevision { get; init; }
    public Guid? WorkRequestId { get; init; }
    public Guid? DependencyTaskId { get; init; }
    public const string QueueSyntax =
        "queue help | queue list <session-id> | queue status <session-id> <task-id> | "
        + "queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> version [after <task-id>] | "
        + "queue cancel|remove <session-id> <generation> <queue-revision> <task-id> <entry-revision> | "
        + "queue clear <session-id> <generation> <queue-revision> confirm | "
        + "queue dispatch <session-id> <generation> <queue-revision>. "
        + "Fixed local-version only; manual fair dispatch of already enqueued current-run work. "
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
        if (operation is "list" && words.Length == 3) { return new(SessionCommandOperation.QueueList, session); }
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
            && ExactId(words[6], out var queuedTask) && words[7].Equals("version", StringComparison.OrdinalIgnoreCase))
        {
            Guid? dependency = null;
            if (words.Length == 10)
            {
                if (!words[8].Equals("after", StringComparison.OrdinalIgnoreCase) || !ExactId(words[9], out var after)) { return Invalid(); }
                dependency = after;
            }
            return new(SessionCommandOperation.QueueEnqueue, session, generation)
            { QueueRevision = revision, WorkRequestId = request, TaskId = queuedTask, DependencyTaskId = dependency };
        }
        return Invalid();
    }
}
