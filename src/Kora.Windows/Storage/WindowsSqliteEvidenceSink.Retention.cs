using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteEvidenceSink
{
    internal DiagnosticRetentionBatch PruneOrdinaryDiagnostics(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        try
        {
            // Retention may only open an already admitted partition, never create or replace one.
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = ValidateDatabase(database.Open(created: false, cancellationToken));
            using var transaction = connection.BeginTransaction();
            checkpoint?.BeforeWrite(connection, transaction);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$cutoff", cutoff.UtcTicks);
            command.CommandText = $"""
                DELETE FROM application_log_events WHERE rowid IN (
                    SELECT rowid FROM application_log_events WHERE due_utc<=$cutoff
                    ORDER BY due_utc,rowid LIMIT {WindowsSqliteDiagnosticRetention.MaximumLogs});
                """;
            var logs = command.ExecuteNonQuery();
            // Validated links share their owner's effective due date and cannot be pruned separately.
            var owners = $"""
                SELECT evidence_id FROM activity_spans WHERE due_utc<=$cutoff
                ORDER BY due_utc,rowid LIMIT {WindowsSqliteDiagnosticRetention.MaximumSpans}
                """;
            command.CommandText = $"DELETE FROM activity_links WHERE evidence_id IN ({owners});";
            var links = command.ExecuteNonQuery();
            command.CommandText = $"DELETE FROM activity_spans WHERE evidence_id IN ({owners});";
            var spans = command.ExecuteNonQuery();
            command.CommandText = """
                SELECT EXISTS(SELECT 1 FROM application_log_events WHERE due_utc<=$cutoff)
                    OR EXISTS(SELECT 1 FROM activity_spans WHERE due_utc<=$cutoff);
                """;
            var hasMore = (long)command.ExecuteScalar()! != 0;
            checkpoint?.BeforeCommit(connection, transaction);
            database.VerifyFiles();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            // Once COMMIT returns, late cancellation cannot turn this durable receipt into cancellation.
            return new(logs, spans, links, hasMore);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested
            && exception is SqliteException or InvalidDataException)
        {
            throw new OperationCanceledException("Diagnostic retention was cancelled before a verified commit.",
                exception, cancellationToken);
        }
    }
}
