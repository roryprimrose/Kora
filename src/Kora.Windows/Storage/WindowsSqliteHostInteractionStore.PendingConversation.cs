using Kora.Core.Hosting;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private bool ConversationPeersCurrent(SqliteConnection connection, Kora.Core.Conversation.ConversationMessage current, bool dispatch)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id FROM host_tasks WHERE session_id=$session AND task_id<>$task AND state IN (0,1,7);";
        command.Parameters.AddWithValue("$session", Id(current.Request.SessionId));
        command.Parameters.AddWithValue("$task", Id(current.Request.TaskId));
        using var reader = command.ExecuteReader();
        var count = 0;
        var dispatched = current.Execution is null ? 0 : 1;
        while (reader.Read())
        {
            if (++count > SessionQueueLimits.MaximumPendingPerSession) { return false; }
            var taskId = new HostId<TaskIdentity>(Guid.Parse(reader.GetString(0)));
            var task = ReadTask(connection, taskId)!;
            var message = ReadConversationMessage(connection, task.Request.RequestId);
            if (!CurrentConversationTask(connection, task, message, current.Generation)
                || dispatch && (task.State == HostTaskState.DispatchRecorded || message!.Sequence < current.Sequence))
            { return false; }
            if (task.State == HostTaskState.DispatchRecorded && ++dispatched > 1) { return false; }
        }
        return true;
    }

    private bool CurrentConversationTask(SqliteConnection connection, HostTaskRecord task,
        Kora.Core.Conversation.ConversationMessage? message, HostRevision generation) =>
        task.State is HostTaskState.IntentRecorded or HostTaskState.DispatchRecorded
        && ReadRun(connection, task.Request.TaskId) == runId && message is not null
        && message.Request == task.Request && message.Generation == generation && message.Outcome is null
        && message.Revision == task.Revision.Value && message.ExpectedTaskState == task.State;

    private void RequirePendingConversationPredecessors(SqliteConnection connection, HostRequest request,
        HostRevision generation, IReadOnlyList<HostRequest> predecessors)
    {
        if (predecessors.Count > SessionQueueLimits.MaximumPendingPerSession
            || predecessors.Distinct().Count() != predecessors.Count
            || predecessors.Any(item => item.SessionId != request.SessionId || item.TaskId == request.TaskId)
            || ReadQuestions(connection, request.SessionId).Any(item => item.Status == Kora.Core.Interaction.QuestionStatus.Pending))
        { throw new InvalidOperationException("Pending conversation admission requires bounded exact current-run predecessors without questions."); }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id FROM host_tasks WHERE session_id=$session AND state IN (0,1,7);";
        command.Parameters.AddWithValue("$session", Id(request.SessionId));
        using var reader = command.ExecuteReader();
        var pending = 0;
        while (reader.Read())
        {
            var taskId = new HostId<TaskIdentity>(Guid.Parse(reader.GetString(0)));
            var task = ReadTask(connection, taskId)!;
            var message = ReadConversationMessage(connection, task.Request.RequestId);
            if (!predecessors.Contains(task.Request) || !CurrentConversationTask(connection, task, message, generation))
            { throw new InvalidOperationException("Other live or Unknown work cannot become pending conversation authority."); }
            if (task.State == HostTaskState.IntentRecorded) { pending++; }
        }
        if (pending >= SessionQueueLimits.MaximumPendingPerSession)
        { throw new InvalidOperationException("The pending conversation queue is full. Remove or defer work; nothing was evicted."); }
    }
}
