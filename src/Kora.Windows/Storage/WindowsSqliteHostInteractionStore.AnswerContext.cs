using System.Collections.Immutable;

using Kora.Core.Conversation;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<ImmutableArray<SubmittedHistoryAvailability>> ReadSubmittedHistoryAvailabilityAsync(
        ConversationMessage answer, CancellationToken token) =>
        new(Task.Run(() =>
        {
            answer.Validate();
            var context = answer.SubmittedContext
                ?? throw new InvalidOperationException("This answer has no recorded source collection.");
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            using var transaction = connection.BeginTransaction();
            var session = RequireSession(connection, answer.Request.SessionId);
            if (session.State == 2 || ReadConversationMessage(connection, answer.Request.RequestId) != answer)
            { throw new InvalidOperationException("The exact retained answer is no longer available."); }
            var states = ImmutableArray.CreateBuilder<SubmittedHistoryAvailability>(context.Sources.Length);
            foreach (var source in context.Sources)
            {
                token.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT session_id,sequence,revision,json_extract(payload,'$.Generation.Value'),
                        json_type(payload,'$.Outcome'),json_type(payload,'$.AssistantResponse')
                    FROM conversation_messages WHERE request_id=$request;
                    """;
                command.Parameters.AddWithValue("$request", Id(source.Request));
                using var reader = command.ExecuteReader();
                var retained = reader.Read() && string.Equals(reader.GetString(0), Id(answer.Request.SessionId), StringComparison.Ordinal)
                    && reader.GetInt64(1) == source.Sequence && reader.GetInt64(2) == source.Revision
                    && reader.GetInt64(3) == source.Generation.Value && string.Equals(reader.GetString(4), "integer", StringComparison.Ordinal)
                    && (source.Role == ConversationHistoryRole.User || !reader.IsDBNull(5)
                        && string.Equals(reader.GetString(5), "object", StringComparison.Ordinal));
                states.Add(retained ? SubmittedHistoryAvailability.RetainedRevision : SubmittedHistoryAvailability.Unavailable);
            }
            database.VerifyFiles();
            token.ThrowIfCancellationRequested();
            return states.MoveToImmutable();
        }, token));
}
