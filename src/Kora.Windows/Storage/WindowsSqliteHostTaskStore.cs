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
    private readonly RestrictedStorageDirectory directory;
    private readonly string databasePath;

    public WindowsSqliteHostTaskStore(IApplicationDataPaths paths)
    {
        directory = new RestrictedStorageDirectory(paths, includeKeys: false);
        databasePath = Path.Combine(directory.Root, "host.db");
    }

    public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (HostActivity.RequireCurrent().Request != record.Request)
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
        if (HostActivity.RequireCurrent().Request != record.Request)
        {
            throw new InvalidOperationException("The queued storage operation lost its live host request.");
        }
        using var boundary = new StorageOperation("storage.task.commit", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = OpenPartition(out var created);
        using var connection = OpenDatabase(created);
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
        VerifyFiles();
        cancellationToken.ThrowIfCancellationRequested();
        // Once COMMIT succeeds, cancellation must not turn a durable receipt into a cancelled result.
        transaction.Commit();
        boundary.Complete();
    }

    private ReadOnlyCollection<HostTaskRecord> ReadIncomplete(int limit, CancellationToken cancellationToken)
    {
        using var boundary = new StorageOperation("storage.task.read", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = OpenPartition(out var created);
        using var connection = OpenDatabase(created);
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
        VerifyFiles();
        boundary.Complete();
        return records.AsReadOnly();
    }

    private FileStream OpenPartition(out bool created)
    {
        created = !Directory.Exists(directory.Root);
        if (created)
        {
            directory.CreateNew();
        }
        return directory.AcquireLease();
    }

    private SqliteConnection OpenDatabase(bool created)
    {
        var isNew = !File.Exists(databasePath);
        if (isNew && !created)
        {
            throw new InvalidDataException("An existing storage partition is missing its database; replacement is forbidden.");
        }
        if (!isNew)
        {
            VerifyFiles();
        }
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = isNew ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 5,
        }.ToString());
        try
        {
            connection.Open();
            VerifyFiles();
            using var settings = connection.CreateCommand();
            settings.CommandText = "PRAGMA synchronous=FULL; PRAGMA temp_store=MEMORY;";
            settings.ExecuteNonQuery();
            if (isNew)
            {
                using var transaction = connection.BeginTransaction();
                using var schema = connection.CreateCommand();
                schema.Transaction = transaction;
                schema.CommandText = $"""
                    CREATE TABLE host_tasks(
                        task_id TEXT PRIMARY KEY NOT NULL,
                        request_id TEXT NOT NULL,
                        session_id TEXT NOT NULL,
                        origin INTEGER NOT NULL,
                        invocation_id TEXT,
                        revision INTEGER NOT NULL CHECK(revision>0),
                        state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 8)) STRICT;
                    CREATE TABLE host_task_events(
                        task_id TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision>0),
                        state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 8),
                        PRIMARY KEY(task_id, revision)) STRICT;
                    PRAGMA application_id={ApplicationId};
                    PRAGMA user_version=1;
                    """;
                schema.ExecuteNonQuery();
                transaction.Commit();
            }
            using var validate = connection.CreateCommand();
            validate.CommandText = "PRAGMA application_id;";
            if (Convert.ToInt64(validate.ExecuteScalar(), CultureInfo.InvariantCulture) != ApplicationId)
            {
                throw new InvalidDataException("The database identity is invalid.");
            }
            validate.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt64(validate.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            {
                throw new InvalidDataException("The database schema version is unsupported.");
            }
            validate.CommandText = "PRAGMA journal_mode;";
            if (!string.Equals(validate.ExecuteScalar() as string, "delete", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The database journal mode is unsupported.");
            }
            validate.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(validate.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The database integrity check failed.");
            }
            validate.CommandText = """
                SELECT count(*) FROM host_tasks t
                WHERE origin NOT IN (0,1,2) OR state NOT BETWEEN 0 AND 8 OR revision<1
                    OR NOT EXISTS(SELECT 1 FROM host_task_events e
                        WHERE e.task_id=t.task_id AND e.revision=t.revision AND e.state=t.state)
                    OR EXISTS(SELECT 1 FROM host_task_events e
                        WHERE e.task_id=t.task_id AND e.revision>t.revision);
                """;
            if (Convert.ToInt64(validate.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
            {
                throw new InvalidDataException("The persisted task state or event projection is invalid.");
            }
            return connection;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            throw new InvalidDataException("The private host database could not be opened or validated.", exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void VerifyFiles()
    {
        directory.Verify();
        foreach (var path in Directory.EnumerateFiles(directory.Root))
        {
            directory.VerifyFile(path);
        }
    }

    private static HostTaskRecord Decode(SqliteDataReader reader)
    {
        static Guid Identifier(SqliteDataReader row, string column)
        {
            if (!Guid.TryParseExact(row.GetString(row.GetOrdinal(column)), "D", out var value) || value == Guid.Empty)
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
