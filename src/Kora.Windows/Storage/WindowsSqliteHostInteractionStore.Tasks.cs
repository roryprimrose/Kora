using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session,
        HostId<TaskIdentity> task, CancellationToken cancellationToken)
    {
        session.Validate();
        task.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            var authority = RequireSession(connection, session);
            if (authority.State == 2) { throw new InvalidOperationException("Removed sessions are not browsable."); }
            var record = ReadTask(connection, task);
            return record?.Request.SessionId == session ? Observe(connection, record, authority.Authority) : null;
        }, cancellationToken));
    }

    public ValueTask AdmitVersionWaitAsync(HostQuestionKey key, CancellationToken token) =>
        new(RunHostMutationAsync(key.Request, "task.wait.version", (connection, transaction, intent, audit) =>
        {
            var session = RequireSession(connection, key.Request.SessionId).Authority;
            var question = ReadQuestions(connection, key.Request.SessionId).SingleOrDefault(q => q.Key == key);
            var observation = ReadObservation(connection, key.Request.RequestId);
            if (intent.Request != key.Request || intent.State != HostTaskState.IntentRecorded || intent.Revision.Value != 1
                || ReadRun(connection, intent.Request.TaskId) != runId
                || key.Request.Origin != RequestOrigin.LocalUi || question is null
                || question.Status != QuestionStatus.Pending || question.SessionGeneration != session.Generation
                || !session.IsActive || !LocalVersionWait.Matches(question)
                || HasOtherPendingQuestion(connection, key)
                || observation is null || observation.RunId != runId || observation.Proposal is not null
                || !observation.Policy.IsUnlocked || observation.Generation != session.Generation)
            {
                throw new InvalidOperationException("An exact original-user pre-dispatch local-version question is required.");
            }
            var wait = new VersionWait(intent.Request.TaskId, key.QuestionId, session.Generation, runId);
            if (question.ExpiresAt <= time.GetUtcNow()) { throw new InvalidOperationException("The local-version question expired."); }
            var sequence = AppendAudit(connection, transaction, intent, session, audit, changes: [WaitChange(wait)]);
            Execute(connection, transaction, "INSERT INTO host_task_waits VALUES($task,$question,$generation,$run,$audit);",
                ("$task", Id(wait.TaskId)), ("$question", Id(wait.QuestionId)), ("$generation", wait.Generation.Value),
                ("$run", wait.RunId.ToString("D")), ("$audit", sequence));
            return true;
        }, token, requireIdle: false).AsTask());

    public ValueTask<HostTaskRecord> AdmitVersionDispatchAsync(HostQuestionKey key, Func<bool> eligible,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        return RunHostMutationAsync(key.Request, "task.admit.version", (connection, transaction, intent, audit) =>
        {
            var session = RequireSession(connection, key.Request.SessionId).Authority;
            var observed = Observe(connection, intent, session);
            var question = observed.Question;
            if (!observed.CurrentSource || !session.IsActive || intent.State != HostTaskState.IntentRecorded
                || question is null || question.Key != key || question.Status != QuestionStatus.Answered
                || question.AnswerChannel != RequestOrigin.LocalUi || question.ExpiresAt <= time.GetUtcNow()
                || !LocalVersionWait.Matches(question)
                || HasOtherPendingQuestion(connection, key))
            {
                throw new InvalidOperationException("The exact current local-version answer cannot admit dispatch.");
            }
            var dispatched = intent.Next(HostTaskState.DispatchRecorded);
            AppendAudit(connection, transaction, intent, session, audit,
                changes: [TaskChange(dispatched)]);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, dispatched);
            return dispatched;
        }, token, eligible, requireIdle: false);
    }

    public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
        Func<bool> canControl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canControl);
        return RunHostMutationAsync(control, "task.cancel.wait", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            var session = RequireSession(connection, control.SessionId).Authority;
            var task = ReadTask(connection, target.TaskId);
            if (control.SessionId != target.SessionId || task is null || task.Request.SessionId != target.SessionId
                || control.TaskId == task.Request.TaskId || control.RequestId == task.Request.RequestId
                || ReadRun(connection, control.TaskId) != runId
                || task.Revision != target.TaskRevision || session.Generation != target.Generation || !session.IsActive)
            {
                throw new InvalidOperationException("The exact session/task revision or generation conflicts.");
            }
            var observed = Observe(connection, task, session);
            var question = observed.Question;
            var original = ReadObservation(connection, task.Request.RequestId);
            if (task.State != HostTaskState.IntentRecorded || task.Request.Origin != RequestOrigin.LocalUi
                || !observed.CurrentSource || question is null || question.Key.QuestionId != target.QuestionId
                || question.Key.Revision != target.QuestionRevision || question.Status != QuestionStatus.Pending
                || question.SessionGeneration != session.Generation || question.ExpiresAt <= time.GetUtcNow()
                || original is null || original.RunId != runId || original.Request != task.Request
                || original.Generation != session.Generation || !original.Policy.IsUnlocked
                || original.Proposal is not null || !LocalVersionWait.Matches(question)
                || HasOtherPendingQuestion(connection, question.Key))
            {
                throw new InvalidOperationException("Cancellation unavailable: only the exact admitted current-run pre-dispatch local-version wait can be cancelled.");
            }
            var cancelled = task.Next(HostTaskState.Cancelled);
            var closed = question with { Key = question.Key.Next(), Status = QuestionStatus.Cancelled };
            var decision = new HostInteractionDecision(HostInteractionOutcome.Cancelled, "pre-dispatch-work-cancelled", closed);
            var sequence = AppendAudit(connection, transaction, intent, session,
                new(audit.CorrelationId, audit.Category, audit.ActionId, SecurityAuditOutcome.Cancelled,
                    audit.Initiator, audit.TargetId, reasonCode: "pre-dispatch-work-cancelled"),
                decision, [TaskChange(cancelled), QuestionChange(closed)]);
            // These three mutations and their trusted audit have exactly one COMMIT.
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, cancelled);
            WriteQuestion(connection, transaction, closed, sequence);
            Execute(connection, transaction, "DELETE FROM host_observations WHERE request_id=$id;", ("$id", Id(task.Request.RequestId)));
            return observed with { Task = cancelled, Question = closed };
        }, cancellationToken, canControl, requireIdle: false);
    }

    private HostTaskObservation Observe(SqliteConnection connection, HostTaskRecord task, WorkSessionAuthorization session)
    {
        var wait = ReadWait(connection, task.Request.TaskId);
        var question = wait is null ? null : ReadQuestions(connection, task.Request.SessionId)
            .SingleOrDefault(q => q.Key.QuestionId == wait.QuestionId);
        return new(task, session.Generation, wait is null ? "unclassified" : LocalVersionWait.Source,
            wait is not null && wait.RunId == runId && wait.Generation == session.Generation, question);
    }

    private static HostTaskRecord? ReadTask(SqliteConnection connection, HostId<TaskIdentity> id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM host_tasks WHERE task_id=$id;";
        command.Parameters.AddWithValue("$id", Id(id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? WindowsSqliteHostTaskStore.Decode(reader) : null;
    }

    private static bool HasOtherPendingQuestion(SqliteConnection connection, HostQuestionKey key) =>
        ReadQuestions(connection, key.Request.SessionId).Any(question =>
            question.Key.Request.TaskId == key.Request.TaskId && question.Key.QuestionId != key.QuestionId
            && question.Status == QuestionStatus.Pending);

    private static void ValidateQuestionTasks(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT session_id FROM host_questions;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            foreach (var question in ReadQuestions(connection, new(ParseId(reader.GetString(0)))))
            {
                var task = ReadTask(connection, question.Key.Request.TaskId);
                if (task is null || !question.Key.Request.IsWithinIntent(task.Request))
                {
                    throw new InvalidDataException("The authoritative question has no exact durable task intent.");
                }
            }
        }
    }

    private static VersionWait? ReadWait(SqliteConnection connection, HostId<TaskIdentity> id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id,question_id,generation,run_id,audit_sequence FROM host_task_waits WHERE task_id=$id;";
        command.Parameters.AddWithValue("$id", Id(id));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return null; }
        var wait = new VersionWait(new(ParseId(reader.GetString(0))), new(ParseId(reader.GetString(1))),
            new(reader.GetInt64(2)), ParseId(reader.GetString(3)));
        ValidateRowAuthority(connection, reader.GetInt64(4), WaitChange(wait));
        return wait;
    }

    private static Guid? ReadRun(SqliteConnection connection, HostId<TaskIdentity> id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT run_id FROM host_task_runs WHERE task_id=$id;";
        command.Parameters.AddWithValue("$id", Id(id));
        return command.ExecuteScalar() is string text ? ParseId(text) : null;
    }

    private static AuthorityChange WaitChange(VersionWait wait) =>
        Change("wait", Id(wait.TaskId), 1, HostInteractionCodec.Encode(wait));
    private static AuthorityChange TaskChange(HostTaskRecord task) =>
        Change("task", Id(task.Request.TaskId), task.Revision.Value, HostInteractionCodec.Encode(task));
    private static void ValidateWaits(SqliteConnection connection)
    {
        using (var runs = connection.CreateCommand())
        {
            runs.CommandText = "SELECT task_id,run_id FROM host_task_runs;";
            using var rows = runs.ExecuteReader();
            while (rows.Read())
            {
                var taskId = new HostId<TaskIdentity>(ParseId(rows.GetString(0)));
                _ = ParseId(rows.GetString(1));
                if (ReadTask(connection, taskId) is null) { throw new InvalidDataException("The host task run binding has no task."); }
            }
        }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id FROM host_task_waits;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = new HostId<TaskIdentity>(ParseId(reader.GetString(0)));
            var wait = ReadWait(connection, id)!;
            var task = ReadTask(connection, id) ?? throw new InvalidDataException("The admitted task wait has no task.");
            var question = ReadQuestions(connection, task.Request.SessionId).SingleOrDefault(q => q.Key.QuestionId == wait.QuestionId);
            if (question is null || question.Key.Request != task.Request || question.SessionGeneration != wait.Generation
                || ReadRun(connection, id) != wait.RunId
                || !LocalVersionWait.Matches(question))
            {
                throw new InvalidDataException("The admitted task/question wait binding is invalid.");
            }
            if (task.State == HostTaskState.Cancelled)
            {
                using var cancelled = connection.CreateCommand();
                cancelled.CommandText = "SELECT audit_sequence FROM host_questions WHERE question_id=$id;";
                cancelled.Parameters.AddWithValue("$id", Id(wait.QuestionId));
                var sequence = (long)(cancelled.ExecuteScalar() ?? throw new InvalidDataException("The cancelled wait has no question."));
                ValidateRowAuthority(connection, sequence, TaskChange(task));
                if (question.Status != QuestionStatus.Cancelled) { throw new InvalidDataException("The atomic cancelled task has a live question."); }
            }
        }
    }

    private sealed record VersionWait(HostId<TaskIdentity> TaskId, HostId<QuestionIdentity> QuestionId,
        HostRevision Generation, Guid RunId);
}
