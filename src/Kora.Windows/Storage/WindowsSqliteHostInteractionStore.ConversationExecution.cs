using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private void MigrateConversationExecution(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateAuthority(connection);
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, "ALTER TABLE conversation_messages RENAME TO previous_conversation_messages;");
        Execute(connection, transaction, HostInteractionSchema.ConversationSchema[1]);
        Execute(connection, transaction, """
            INSERT INTO conversation_messages SELECT * FROM previous_conversation_messages;
            DROP TABLE previous_conversation_messages;
            """);
        ValidateAuthority(connection);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<ConversationSubmissionReceipt> BeginConversationExecutionAsync(HostRequest request,
        ModelTurnProvenance provenance, long metadataRevision, Func<bool> admitted, CancellationToken token) =>
        RunHostMutationAsync(request, "conversation.execution", (connection, transaction, intent, audit) =>
        {
            var previous = ReadConversationMessage(connection, request.RequestId)
                ?? throw new InvalidDataException("The exact original submission is missing.");
            var session = RequireSession(connection, request.SessionId);
            if (provenance.Request != request || provenance.Selection is null || !provenance.Selection.IsValid
                || !ConversationResponse.SupportsProvider(provenance.Selection.Provider)
                || previous.Request != request || previous.Revision != 1 || previous.Version != 1
                || previous.Outcome is not null || session.State != 0
                || previous.Handoff is { } handoff && (provenance.Selection != handoff.Destination
                    || time.GetUtcNow() < handoff.CreatedAt || time.GetUtcNow() >= handoff.ExpiresAt)
                || metadataRevision < 0 || (ReadMetadata(connection, request.SessionId)?.Revision.Value ?? 0) != metadataRevision
                || session.Authority.Generation != previous.Generation || intent.State != HostTaskState.IntentRecorded
                || intent.Revision.Value != 1 || ReadRun(connection, request.TaskId) != runId
                || !ConversationPeersCurrent(connection, previous, dispatch: true))
            { throw new InvalidOperationException("Only the exact retained original current-run request may begin an admitted conversation turn."); }
            provenance.Turn.Validate();
            var message = previous with { Version = 2, Revision = 2, Execution = provenance, ExecutionMetadataRevision = metadataRevision };
            message.Validate();
            var dispatched = intent.Next(HostTaskState.DispatchRecorded);
            var sequence = AppendAudit(connection, transaction, intent, session.Authority, audit,
                changes: [MessageChange(message), TaskChange(dispatched)], requestedAudit: ConversationRequestedAudit(audit));
            WriteConversationMessage(connection, transaction, message, sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, dispatched);
            TouchActivity(connection, transaction, request.SessionId);
            return new ConversationSubmissionReceipt(message, dispatched);
        }, token, admitted, requireIdle: false);

    public ValueTask<ConversationSubmissionReceipt> CompleteConversationExecutionAsync(HostRequest request,
        ModelTurnResult result, Func<bool> admitted, CancellationToken token)
    {
        if (!Enum.IsDefined(result.Outcome) || !Enum.IsDefined(result.Reason)
            || result.Provenance is null || result.Provenance.Request != request
            || result.Outcome != ModelTurnOutcome.Succeeded && (result.Response is not null || result.Observations is not null || result.SubmittedContext is not null))
        { throw new InvalidDataException("The admitted execution terminal result is invalid."); }
        return RunHostMutationAsync(request, "conversation.answer", (connection, transaction, intent, audit) =>
        {
            var previous = ReadConversationMessage(connection, request.RequestId)
                ?? throw new InvalidDataException("The exact retained original execution is missing.");
            var session = RequireSession(connection, request.SessionId);
            if (previous.Request != request || previous.Version != 2 || previous.Revision != 2
                || previous.Execution != result.Provenance || previous.Outcome is not null || session.State != 0
                || (ReadMetadata(connection, request.SessionId)?.Revision.Value ?? 0) != previous.ExecutionMetadataRevision
                || session.Authority.Generation != previous.Generation || intent.State != HostTaskState.DispatchRecorded
                || intent.Revision.Value != 2 || ReadRun(connection, request.TaskId) != runId)
            { throw new InvalidOperationException("The original execution or terminal outcome conflicts; inspect without replay."); }
            if (result.SubmittedContext is { } context)
            {
                context.Validate();
                foreach (var source in context.Sources)
                {
                    var original = ReadConversationMessage(connection, source.Request);
                    if (original is null || original.Request.SessionId != request.SessionId
                        || original.Generation != source.Generation || original.Sequence != source.Sequence
                        || original.Revision != source.Revision || original.Outcome is null
                        || source.Role == ConversationHistoryRole.Assistant && original.AssistantResponse is null)
                    { throw new InvalidOperationException("The exact submitted history source changed before answer retention."); }
                }
            }
            var message = previous with
            {
                Revision = 3, Outcome = result.Outcome, Reason = result.Reason,
                AssistantResponse = result.Outcome == ModelTurnOutcome.Succeeded
                    ? ConversationResponse.FromResult(result, previous.Execution!) : null,
                Observations = result.Observations,
                SubmittedContext = result.SubmittedContext,
            };
            message.Validate();
            var completed = intent.Next(message.ExpectedTaskState);
            var terminalAudit = new SecurityAuditEvent(audit.CorrelationId, audit.Category, audit.ActionId,
                completed.State == HostTaskState.Succeeded ? SecurityAuditOutcome.Succeeded
                    : completed.State == HostTaskState.Unknown ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Denied,
                audit.Initiator, audit.TargetId, reasonCode: result.Reason.ToString().ToLowerInvariant());
            var sequence = AppendAudit(connection, transaction, intent, session.Authority, terminalAudit,
                changes: [MessageChange(message), TaskChange(completed)], requestedAudit: ConversationRequestedAudit(terminalAudit));
            WriteConversationMessage(connection, transaction, message, sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            TouchActivity(connection, transaction, request.SessionId);
            return new ConversationSubmissionReceipt(message, completed);
        }, token, admitted, requireIdle: false);
    }
}
