using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Interaction.ConversationNotices;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private ConversationNoticeAuthority? ObserveConversationNotice(SqliteConnection connection,
        HostTaskRecord task, ConversationMessage message, WorkSessionAuthorization session)
    {
        if (!session.IsActive || message.Generation != session.Generation || message.Version != 2
            || message.Execution is null || task.Revision.Value != message.Revision
            || task.State != message.ExpectedTaskState || ReadRun(connection, task.Request.TaskId) != runId
            || (ReadMetadata(connection, task.Request.SessionId)?.Revision.Value ?? 0) != message.ExecutionMetadataRevision
            || message.Reason == ModelTurnReason.HostInterrupted)
        { return null; }
        HostQuestionRecord? question = null;
        if (message.Outcome is null)
        {
            if (!ConversationQuestionsPermitObservation(connection, task, message, session)) { return null; }
            question = ReadQuestions(connection, task.Request.SessionId).SingleOrDefault(item =>
                item.Status == QuestionStatus.Pending && item.Key.Request == task.Request
                && item.Proposal is null && item.AnswerChannel is null
                && string.Equals(item.Spec.Purpose, LocalModelQuestionPolicy.Purpose, StringComparison.Ordinal)
                && string.Equals(item.Spec.SourceId, LocalModelQuestionPolicy.SourceId, StringComparison.Ordinal));
            if (question is null) { return null; }
        }
        var notice = new ConversationNoticeAuthority(task.Request, message.Generation, message.Revision,
            message.Execution.Turn, message.SubmittedAt, message.Outcome, message.Reason, question?.Key, question?.ExpiresAt);
        notice.Validate(task);
        return notice;
    }
}
