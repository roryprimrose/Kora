using System.Collections.Immutable;

using Kora.Core.Conversation;
using Kora.Core.Memory;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<ImmutableArray<SubmittedHistoryAvailability>> ReadSubmittedHistoryAvailabilityAsync(
        ConversationMessage answer, CancellationToken token) =>
        ReadSubmittedContextAvailabilityAsync(answer, token);

    public ValueTask<ImmutableArray<SubmittedHistoryAvailability>> ReadSubmittedContextAvailabilityAsync(
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
            var states = ImmutableArray.CreateBuilder<SubmittedHistoryAvailability>(context.SourceCount);
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
            if (!context.MemorySources.IsDefault)
            {
                foreach (var source in context.MemorySources)
                {
                    token.ThrowIfCancellationRequested();
                    states.Add(IsSubmittedMemoryRetained(connection, transaction, source)
                        ? SubmittedHistoryAvailability.RetainedRevision : SubmittedHistoryAvailability.Unavailable);
                }
            }
            if (!context.ProfileMemorySources.IsDefault)
            {
                var profile = ReadMemoryProfile(connection);
                foreach (var source in context.ProfileMemorySources)
                {
                    token.ThrowIfCancellationRequested();
                    states.Add(source.Profile == profile && IsSubmittedProfileMemoryRetained(connection, transaction, source)
                        ? SubmittedHistoryAvailability.RetainedRevision : SubmittedHistoryAvailability.Unavailable);
                }
            }
            database.VerifyFiles();
            token.ThrowIfCancellationRequested();
            return states.MoveToImmutable();
        }, token));

    private static bool IsSubmittedMemoryRetained(SqliteConnection connection, SqliteTransaction transaction,
        SubmittedMemorySource source)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM reviewed_memory WHERE memory_id=$id AND session_id=$session AND revision=$revision
                AND json_extract(payload,'$.Id.Value')=$id
                AND json_extract(payload,'$.Session.Value')=$session
                AND json_extract(payload,'$.Revision.Value')=$revision
                AND json_extract(payload,'$.ScopeKind') IS NULL
                AND json_extract(payload,'$.Review')=$review AND json_extract(payload,'$.Retention')=$retention;
            """;
        command.Parameters.AddWithValue("$id", Id(source.Id));
        command.Parameters.AddWithValue("$session", Id(source.Session));
        command.Parameters.AddWithValue("$revision", source.Revision.Value);
        command.Parameters.AddWithValue("$review", (int)MemoryReviewState.Admitted);
        command.Parameters.AddWithValue("$retention", (int)MemoryRetentionState.Enabled);
        return command.ExecuteScalar() is not null;
    }

    private static bool IsSubmittedProfileMemoryRetained(SqliteConnection connection, SqliteTransaction transaction,
        SubmittedProfileMemorySource source)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM reviewed_memory WHERE memory_id=$id AND revision=$revision
                AND json_extract(payload,'$.Id.Value')=$id
                AND json_extract(payload,'$.Revision.Value')=$revision
                AND json_extract(payload,'$.ScopeKind')=$scope
                AND json_extract(payload,'$.Lineage.Profile.Value')=$profile
                AND json_extract(payload,'$.Review')=$review AND json_extract(payload,'$.Retention')=$retention;
            """;
        command.Parameters.AddWithValue("$id", Id(source.Id));
        command.Parameters.AddWithValue("$revision", source.Revision.Value);
        command.Parameters.AddWithValue("$scope", (int)MemoryScopeKind.DeviceProfile);
        command.Parameters.AddWithValue("$profile", Id(source.Profile));
        command.Parameters.AddWithValue("$review", (int)MemoryReviewState.Admitted);
        command.Parameters.AddWithValue("$retention", (int)MemoryRetentionState.Enabled);
        return command.ExecuteScalar() is not null;
    }
}
