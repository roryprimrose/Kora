using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    /// <summary>Revalidates a held ordinary conversation question against its current-run source and audited exact bytes.</summary>
    public ValueTask<bool> ValidateConversationNoticeQuestionAsync(
        HostQuestionRecord original, LocalEvent notice, CancellationToken token) => new(Task.Run(() =>
    {
        notice.Validate();
        using var lease = database.AcquireReadLease(token);
        using var connection = Open(created: false, token);
        var key = original.Key;
        var session = RequireSession(connection, key.Request.SessionId).Authority;
        var task = ReadTask(connection, key.Request.TaskId);
        if (notice.Source != LocalEventSource.ConversationQuestion || notice.SessionId != key.Request.SessionId
            || notice.TaskId != key.Request.TaskId || notice.SubjectId != key.QuestionId.Value
            || notice.SourceRevision != key.Revision.Value || notice.Generation != original.SessionGeneration.Value
            || notice.Origin != key.Request.Origin || notice.ExpiresAt != original.ExpiresAt
            || original.Status != QuestionStatus.Pending || original.Proposal is not null || original.AnswerChannel is not null
            || original.ExpiresAt <= time.GetUtcNow() || task is null)
        { return false; }
        var source = ObserveConversationTask(connection, task, session);
        if (source is not { CurrentSource: true, ConversationNotice.Question: { } question }
            || question != key || source.ConversationNotice.MessageRevision != notice.RelatedRevision
            || source.ConversationNotice.QuestionExpiresAt != original.ExpiresAt)
        { return false; }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision,audit_sequence FROM host_questions WHERE question_id=$question AND request_id=$request AND session_id=$session;";
        command.Parameters.AddWithValue("$question", Id(key.QuestionId));
        command.Parameters.AddWithValue("$request", Id(key.Request.RequestId));
        command.Parameters.AddWithValue("$session", Id(key.Request.SessionId));
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != key.Revision.Value) { return false; }
        ValidateRowAuthority(connection, reader.GetInt64(1), QuestionChange(original));
        return original.ExpiresAt > time.GetUtcNow();
    }, token));

    /// <summary>Validates an already-held original wait using metadata and its trusted digest; never loads a question body.</summary>
    public ValueTask<bool> ValidateQuestionFocusAsync(HostQuestionRecord original, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var key = original.Key;
            var session = RequireSession(connection, key.Request.SessionId).Authority;
            var task = ReadTask(connection, key.Request.TaskId);
            var wait = ReadWait(connection, key.Request.TaskId);
            if (!session.IsActive || session.Generation != original.SessionGeneration
                || task is not { State: HostTaskState.IntentRecorded, Revision.Value: 1 }
                || task.Request != key.Request || ReadRun(connection, key.Request.TaskId) != runId
                || wait is null || wait.RunId != runId || wait.Generation != session.Generation
                || wait.QuestionId != key.QuestionId || original.Status != QuestionStatus.Pending
                || original.ExpiresAt <= time.GetUtcNow() || !LocalVersionWait.Matches(original))
            { return false; }
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT revision,audit_sequence FROM host_questions
                WHERE question_id=$question AND request_id=$request AND session_id=$session;
                """;
            command.Parameters.AddWithValue("$question", Id(key.QuestionId));
            command.Parameters.AddWithValue("$request", Id(key.Request.RequestId));
            command.Parameters.AddWithValue("$session", Id(key.Request.SessionId));
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(0) != key.Revision.Value) { return false; }
            ValidateRowAuthority(connection, reader.GetInt64(1), QuestionChange(original));
            return original.ExpiresAt > time.GetUtcNow();
        }, token));
    }
}
