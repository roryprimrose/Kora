using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Network;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private bool ConversationQuestionsPermitObservation(SqliteConnection connection, HostTaskRecord task,
        ConversationMessage message, WorkSessionAuthorization session)
    {
        var pending = ReadQuestions(connection, task.Request.SessionId)
            .Where(question => question.Status == QuestionStatus.Pending).Take(2).ToArray();
        if (pending.Length == 0) { return true; }
        if (pending.Length != 1 || task.State != HostTaskState.DispatchRecorded || message.Version != 2
            || message.Execution is null)
        { return false; }
        var question = pending[0];
        var spec = question.Spec;
        if (ReadObservation(connection, task.Request.RequestId) is { } observation
            && observation.RunId == runId && observation.Generation == session.Generation
            && observation.LocalHandoff is { } local && local.Execution == message.Execution
            && local.TaskRevision == task.Revision && local.Question == question.Key
            && question.Key.Request == task.Request && question.SessionGeneration == session.Generation
            && question.Proposal is null && question.ExpiresAt == local.ExpiresAt
            && time.GetUtcNow() >= local.CreatedAt && time.GetUtcNow() < local.ExpiresAt
            && SameQuestionSpec(spec, LocalHandoffQuestion()))
        { return true; }
        if (question.Proposal is { } proposal && question.Key.Request.IsWithinIntent(task.Request)
            && question.Key.Request.InvocationId is not null && proposal.Request == question.Key.Request
            && question.SessionGeneration == session.Generation && proposal.ExpiresAt > time.GetUtcNow()
            && question.ExpiresAt == proposal.ExpiresAt && proposal.Effect == HostOperationEffect.BoundedRead
            && string.Equals(proposal.Binding.ActionId, WebPageAccessBinding.ActionId, StringComparison.Ordinal)
            && string.Equals(spec.Purpose, "authorization", StringComparison.Ordinal)
            && string.Equals(spec.SourceId, "local-web-page", StringComparison.Ordinal)
            && spec.Kind == QuestionKind.SingleChoice && spec.Options.SequenceEqual([new QuestionOption("once", "Approve once")]))
        { return true; }
        if (question.Key.Request != task.Request || question.SessionGeneration != session.Generation
            || question.Proposal is not null || question.ExpiresAt <= time.GetUtcNow()
            || !string.Equals(spec.Purpose, LocalModelQuestionPolicy.Purpose, StringComparison.Ordinal)
            || !string.Equals(spec.SourceId, LocalModelQuestionPolicy.SourceId, StringComparison.Ordinal)
            || spec.Kind is not (QuestionKind.Text or QuestionKind.SingleChoice))
        { return false; }
        var expected = LocalModelQuestionPolicy.CreateSpec(new(spec.Text, spec.Options.Select(option => option.Label).ToArray()));
        return spec.Kind == expected.Kind && spec.Minimum == expected.Minimum && spec.Maximum == expected.Maximum
            && spec.MaximumTextLength == expected.MaximumTextLength && spec.Options.SequenceEqual(expected.Options);
    }
}
