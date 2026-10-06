using System.Collections.ObjectModel;
using System.Globalization;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed class WindowsSqliteHostTaskStore : IHostTaskStore
{
    private const int ApplicationId = 1263489585;
    private static readonly string[] Schema =
    [
        """
        CREATE TABLE host_tasks(
            task_id TEXT PRIMARY KEY NOT NULL,
            request_id TEXT NOT NULL,
            session_id TEXT NOT NULL,
            origin INTEGER NOT NULL,
            invocation_id TEXT,
            revision INTEGER NOT NULL CHECK(revision>0),
            state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 8)) STRICT
        """,
        """
        CREATE TABLE host_task_events(
            task_id TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision>0),
            state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 8),
            PRIMARY KEY(task_id, revision)) STRICT
        """,
    ];
    private readonly RestrictedSqliteDatabase database;

    public WindowsSqliteHostTaskStore(IApplicationDataPaths paths)
    {
        database = new RestrictedSqliteDatabase(paths, "HostStorageV1", "host.db", ApplicationId, Schema);
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = database.AcquireLease(out var created, cancellationToken);
            using var connection = OpenDatabase(created, cancellationToken);
        }, cancellationToken));

    public ValueTask<HostTaskRecord?> ReadTaskAsync(HostId<TaskIdentity> taskId, CancellationToken cancellationToken)
    {
        taskId.Validate();
        return new ValueTask<HostTaskRecord?>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = database.AcquireLease(out var created, cancellationToken);
            using var connection = OpenDatabase(created, cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM host_tasks WHERE task_id=$task;";
            command.Parameters.AddWithValue("$task", taskId.Value.ToString("D"));
            using var reader = command.ExecuteReader();
            var record = reader.Read() ? Decode(reader) : null;
            database.VerifyFiles();
            return record;
        }, cancellationToken));
    }

    public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Request != record.Request)
        {
            throw new InvalidOperationException("The host activity does not own this request.");
        }
        if (expectedRevision < 0 || record.Revision.Value != checked(expectedRevision + 1))
        {
            throw new InvalidOperationException("The expected task revision is invalid.");
        }
        return new ValueTask(Task.Run(() => Commit(record, expectedRevision, cancellationToken), cancellationToken));
    }

    public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        return new ValueTask<IReadOnlyList<HostTaskRecord>>(
            Task.Run<IReadOnlyList<HostTaskRecord>>(() => ReadIncomplete(limit, cancellationToken), cancellationToken));
    }

    private void Commit(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
    {
        var live = HostActivity.RequireCurrent();
        if (live.Activity!.IsStopped || live.Request != record.Request)
        {
            throw new InvalidOperationException("The queued storage operation lost its live host request.");
        }
        using var boundary = new StorageOperation("storage.task.commit", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = database.AcquireLease(out var created, cancellationToken);
        using var connection = OpenDatabase(created, cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var current = connection.CreateCommand();
        current.Transaction = transaction;
        current.CommandText = "SELECT * FROM host_tasks WHERE task_id=$task;";
        current.Parameters.AddWithValue("$task", record.Request.TaskId.Value.ToString("D"));
        HostTaskRecord? previous;
        using (var reader = current.ExecuteReader())
        {
            previous = reader.Read() ? Decode(reader) : null;
        }
        if (expectedRevision == 0)
        {
            if (previous is not null || record.State != HostTaskState.IntentRecorded)
            {
                throw new InvalidOperationException("A task intent must be new.");
            }
        }
        else if (previous is null || previous.Revision.Value != expectedRevision
                 || previous.Request != record.Request || previous.Next(record.State) != record)
        {
            throw new InvalidOperationException("The task revision, identity or transition conflicts with durable state.");
        }

        using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = """
            INSERT INTO host_task_events(task_id, revision, state) VALUES($task, $revision, $state);
            INSERT INTO host_tasks(task_id, request_id, session_id, origin, invocation_id, revision, state)
            VALUES($task, $request, $session, $origin, $invocation, $revision, $state)
            ON CONFLICT(task_id) DO UPDATE SET revision=excluded.revision, state=excluded.state;
            """;
        write.Parameters.AddWithValue("$task", record.Request.TaskId.Value.ToString("D"));
        write.Parameters.AddWithValue("$request", record.Request.RequestId.Value.ToString("D"));
        write.Parameters.AddWithValue("$session", record.Request.SessionId.Value.ToString("D"));
        write.Parameters.AddWithValue("$origin", (int)record.Request.Origin);
        write.Parameters.AddWithValue("$invocation", record.Request.InvocationId is { } invocation
            ? invocation.Value.ToString("D") : DBNull.Value);
        write.Parameters.AddWithValue("$revision", record.Revision.Value);
        write.Parameters.AddWithValue("$state", (int)record.State);
        write.ExecuteNonQuery();
        database.VerifyFiles();
        cancellationToken.ThrowIfCancellationRequested();
        // Once COMMIT succeeds, cancellation must not turn a durable receipt into a cancelled result.
        transaction.Commit();
        boundary.Complete();
    }

    private ReadOnlyCollection<HostTaskRecord> ReadIncomplete(int limit, CancellationToken cancellationToken)
    {
        using var boundary = new StorageOperation("storage.task.read", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = database.AcquireLease(out var created, cancellationToken);
        using var connection = OpenDatabase(created, cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM host_tasks WHERE state IN (0,1) ORDER BY task_id LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var records = new List<HostTaskRecord>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.Add(Decode(reader));
        }
        database.VerifyFiles();
        boundary.Complete();
        return records.AsReadOnly();
    }

    private SqliteConnection OpenDatabase(bool created, CancellationToken cancellationToken)
    {
        var connection = database.Open(created, cancellationToken);
        try
        {
            using var validate = connection.CreateCommand();
            validate.CommandText = """
                SELECT count(*) FROM host_tasks t
                WHERE origin NOT IN (0,1,2) OR state NOT BETWEEN 0 AND 8 OR revision NOT BETWEEN 1 AND 3
                    OR length(task_id)<>36 OR length(request_id)<>36 OR length(session_id)<>36
                    OR (invocation_id IS NOT NULL AND length(invocation_id)<>36)
                    OR NOT EXISTS(SELECT 1 FROM host_task_events e
                        WHERE e.task_id=t.task_id AND e.revision=t.revision AND e.state=t.state)
                    OR EXISTS(SELECT 1 FROM host_task_events e
                        WHERE e.task_id=t.task_id AND e.revision>t.revision)
                    OR (SELECT count(*) FROM host_task_events e WHERE e.task_id=t.task_id)<>t.revision;
                """;
            if (Convert.ToInt64(validate.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
            {
                throw new InvalidDataException("The persisted task state or event projection is invalid.");
            }
            ValidateLedger(connection);
            return connection;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidDataException("The private host database could not be opened or validated.", exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ValidateLedger(SqliteConnection connection)
    {
        using var tasks = connection.CreateCommand();
        tasks.CommandText = "SELECT * FROM host_tasks;";
        using var reader = tasks.ExecuteReader();
        var count = 0L;
        while (reader.Read())
        {
            var task = Decode(reader);
            using var events = connection.CreateCommand();
            events.CommandText = "SELECT revision,state FROM host_task_events WHERE task_id=$task ORDER BY revision;";
            events.Parameters.AddWithValue("$task", task.Request.TaskId.Value.ToString("D"));
            using var ledger = events.ExecuteReader();
            HostTaskRecord? previous = null;
            while (ledger.Read())
            {
                var record = new HostTaskRecord(task.Request, new(ledger.GetInt64(0)), (HostTaskState)ledger.GetInt64(1));
                if (previous is null
                    ? record.Revision.Value != 1 || record.State != HostTaskState.IntentRecorded
                    : record.Revision.Value != previous.Revision.Value + 1 || previous.IsTerminal)
                {
                    throw new InvalidDataException("The persisted task ledger is discontinuous or replays terminal work.");
                }
                if (previous is not null)
                {
                    try
                    {
                        if (previous.Next(record.State) != record)
                        {
                            throw new InvalidDataException("The persisted task ledger transition is invalid.");
                        }
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new InvalidDataException("The persisted task ledger transition is invalid.", exception);
                    }
                }
                previous = record;
                count++;
            }
            if (previous != task)
            {
                throw new InvalidDataException("The task ledger and durable receipt disagree.");
            }
        }
        using var total = connection.CreateCommand();
        total.CommandText = "SELECT count(*) FROM host_task_events;";
        if (Convert.ToInt64(total.ExecuteScalar(), CultureInfo.InvariantCulture) != count)
        {
            throw new InvalidDataException("The task ledger contains orphan events.");
        }
    }

    private static HostTaskRecord Decode(SqliteDataReader reader)
    {
        static Guid Identifier(SqliteDataReader row, string column)
        {
            var text = row.GetString(row.GetOrdinal(column));
            if (!Guid.TryParseExact(text, "D", out var value) || value == Guid.Empty
                || !string.Equals(text, value.ToString("D"), StringComparison.Ordinal))
            {
                throw new InvalidDataException("A persisted host identity is invalid.");
            }
            return value;
        }
        var originValue = reader.GetInt64(reader.GetOrdinal("origin"));
        var stateValue = reader.GetInt64(reader.GetOrdinal("state"));
        if (originValue is < int.MinValue or > int.MaxValue || stateValue is < int.MinValue or > int.MaxValue
            || !Enum.IsDefined((RequestOrigin)originValue) || !Enum.IsDefined((HostTaskState)stateValue))
        {
            throw new InvalidDataException("A persisted host origin or task state is invalid.");
        }
        var request = new HostRequest(new(Identifier(reader, "request_id")),
            new(Identifier(reader, "session_id")), new(Identifier(reader, "task_id")),
            (RequestOrigin)originValue,
            reader.IsDBNull(reader.GetOrdinal("invocation_id")) ? null : new(Identifier(reader, "invocation_id")));
        return new HostTaskRecord(request, new HostRevision(reader.GetInt64(reader.GetOrdinal("revision"))),
            (HostTaskState)stateValue);
    }
}
