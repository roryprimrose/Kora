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
    internal static readonly string[] Schema =
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
    private RestrictedSqliteDatabase database;
    private readonly RestrictedSqliteDatabase legacy;
    private readonly RestrictedStorageDirectory interactionPartition;
    private Action<SqliteConnection>? validateAuthority;
    private Guid? authorityRun;
    private readonly ISqliteTransactionCheckpoint? checkpoint;

    public WindowsSqliteHostTaskStore(IApplicationDataPaths paths)
        : this(paths, checkpoint: null)
    {
    }

    internal WindowsSqliteHostTaskStore(IApplicationDataPaths paths, ISqliteTransactionCheckpoint? checkpoint)
    {
        legacy = new RestrictedSqliteDatabase(paths, "HostStorageV1", "host.db", ApplicationId,
            LegacySchema, new(1, 2, Schema, (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                ValidateTasks(connection);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = HandoffTable + "; INSERT INTO task_authority_handoff VALUES(1,0);";
                command.ExecuteNonQuery();
            }));
        database = legacy;
        interactionPartition = new(paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
        this.checkpoint = checkpoint;
    }

    private const string HandoffTable = """
        CREATE TABLE task_authority_handoff(
            singleton INTEGER PRIMARY KEY NOT NULL CHECK(singleton=1),
            frozen INTEGER NOT NULL CHECK(frozen IN (0,1))) STRICT
        """;
    private static readonly string[] LegacySchema = [.. Schema, HandoffTable];

    internal void BindAuthority(RestrictedSqliteDatabase authority, Action<SqliteConnection> validate, Guid runId)
    {
        database = authority;
        validateAuthority = validate;
        authorityRun = runId;
    }

    internal void RequireFreshAuthority(CancellationToken token)
    {
        using var lease = legacy.AcquireReadLease(token);
        using var source = legacy.Open(created: false, token);
        ValidateTasks(source);
        using var command = source.CreateCommand();
        command.CommandText = "SELECT (SELECT count(*) FROM host_tasks)+(SELECT frozen FROM task_authority_handoff WHERE singleton=1);";
        if (command.ExecuteScalar() is not long value || value != 0)
        {
            throw new InvalidDataException("Interaction authority is missing beside existing/frozen tasks. Restore or explicitly recover storage; replacement is forbidden.");
        }
    }

    internal void ImportAuthority(SqliteConnection destination, SqliteTransaction transaction, CancellationToken token)
    {
        using var lease = legacy.AcquireReadLease(token);
        using var source = legacy.Open(created: false, token);
        ValidateTasks(source);
        using (var freeze = source.CreateCommand())
        {
            freeze.CommandText = "UPDATE task_authority_handoff SET frozen=1 WHERE singleton=1;";
            if (freeze.ExecuteNonQuery() != 1) { throw new InvalidDataException("The legacy task handoff is missing."); }
        }
        foreach (var table in new[] { "host_tasks", "host_task_events" })
        {
            using var read = source.CreateCommand();
            read.CommandText = "SELECT * FROM " + table + ";";
            using var rows = read.ExecuteReader();
            while (rows.Read())
            {
                token.ThrowIfCancellationRequested();
                using var write = destination.CreateCommand();
                write.Transaction = transaction;
                var parameters = Enumerable.Range(0, rows.FieldCount).Select(index => "$p" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
                write.CommandText = "INSERT INTO " + table + " VALUES(" + string.Join(',', parameters) + ");";
                for (var index = 0; index < rows.FieldCount; index++)
                {
                    write.Parameters.AddWithValue(parameters[index], rows.GetValue(index));
                }
                write.ExecuteNonQuery();
            }
        }
        legacy.VerifyFiles();
        ValidateTasks(destination);
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (validateAuthority is null && !legacy.HasExistingPartition() && interactionPartition.HasExistingPartition())
            {
                throw new InvalidDataException("The legacy task handoff partition is missing beside interaction authority. Replacement is forbidden.");
            }
            using var lease = AcquireTaskLease(out var created, cancellationToken);
            using var connection = OpenDatabase(created, cancellationToken, allowFrozen: true);
        }, cancellationToken));

    internal void RequireRetiredAuthority(CancellationToken token)
    {
        using var lease = legacy.AcquireReadLease(token);
        using var source = legacy.Open(created: false, token);
        ValidateTasks(source);
        using var command = source.CreateCommand();
        command.CommandText = "SELECT frozen FROM task_authority_handoff WHERE singleton=1;";
        if (command.ExecuteScalar() is not long frozen || frozen != 1)
        {
            throw new InvalidDataException("The consolidated task authority has no validated frozen handoff. Recovery is required.");
        }
    }

    // The interaction writer holds this same lease until its own SQLite COMMIT finishes.
    // No task cancellation/terminal transition can interleave with admitted authority.
    internal T WithCommittedIntent<T>(HostRequest request, Func<HostTaskRecord, T> operation,
        CancellationToken cancellationToken, bool requireIdle = false)
        => WithCommittedIntent(request, (_, intent) => operation(intent), cancellationToken, requireIdle);

    internal T WithCommittedIntent<T>(HostRequest request, Func<SqliteConnection, HostTaskRecord, T> operation,
        CancellationToken cancellationToken, bool requireIdle = false)
    {
        RequireCommittedIntentContext(request);
        using var lease = AcquireTaskLease(out var created, cancellationToken);
        using var connection = OpenDatabase(created, cancellationToken);
        var intent = ReadCommittedIntent(connection, request, requireIdle);
        RequireNotDisposed(connection, request.SessionId);
        var result = operation(connection, intent);
        database.VerifyFiles();
        return result;
    }

    internal async Task<T> WithCommittedIntentAsync<T>(HostRequest request,
        Func<SqliteConnection, HostTaskRecord, Task<T>> operation, CancellationToken cancellationToken)
    {
        RequireCommittedIntentContext(request);
        using var lease = AcquireTaskLease(out var created, cancellationToken);
        using var connection = OpenDatabase(created, cancellationToken);
        var intent = ReadCommittedIntent(connection, request, requireIdle: false);
        RequireNotDisposed(connection, request.SessionId);
        var result = await operation(connection, intent).ConfigureAwait(false);
        database.VerifyFiles();
        return result;
    }

    private static void RequireCommittedIntentContext(HostRequest request)
    {
        var live = HostActivity.RequireCurrent();
        if (live.Activity!.IsStopped || live.Request != request)
        {
            throw new InvalidOperationException("The interaction lost its live owning host request.");
        }
    }

    private static HostTaskRecord ReadCommittedIntent(SqliteConnection connection, HostRequest request, bool requireIdle)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM host_tasks WHERE task_id=$task;";
        command.Parameters.AddWithValue("$task", request.TaskId.Value.ToString("D"));
        HostTaskRecord? intent;
        using (var reader = command.ExecuteReader())
        {
            intent = reader.Read() ? Decode(reader) : null;
        }
        if (intent is null || intent.IsTerminal || !request.IsWithinIntent(intent.Request))
        {
            throw new InvalidDataException("Interaction authority requires matching committed nonterminal intent.");
        }
        if (requireIdle)
        {
            command.CommandText = """
                SELECT * FROM host_tasks WHERE session_id=$session AND state IN (0,1,7)
                ORDER BY task_id LIMIT 101;
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$session", request.SessionId.Value.ToString("D"));
            using var blockers = command.ExecuteReader();
            var count = 0;
            while (blockers.Read())
            {
                var blocker = Decode(blockers);
                if (++count > 100 || blocker != intent)
                {
                    throw new InvalidOperationException("Session lifecycle is blocked by nonterminal/Unknown work or blocker overflow. No work was abandoned.");
                }
            }
        }
        return intent;
    }

    public ValueTask<HostTaskRecord?> ReadTaskAsync(HostId<TaskIdentity> taskId, CancellationToken cancellationToken)
    {
        taskId.Validate();
        return new ValueTask<HostTaskRecord?>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = AcquireTaskLease(out var created, cancellationToken);
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

    internal T WithCommittedIdleIntent<T>(HostRequest request, Func<HostTaskRecord, T> operation,
        CancellationToken cancellationToken) =>
        WithCommittedIntent(request, intent =>
        {
            if (intent.State != HostTaskState.IntentRecorded || intent.Revision.Value != 1
                || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            {
                throw new InvalidOperationException("Lifecycle control requires a fresh original-user control intent.");
            }
            // WithCommittedIntent owns the task lease. Use that same connection/lease rather
            // than a passive UI query or a second acquisition of the non-reentrant lease.
            return operation(intent);
        }, cancellationToken, requireIdle: true);

    internal T WithCommittedIdleIntent<T>(HostRequest request, Func<SqliteConnection, HostTaskRecord, T> operation,
        CancellationToken cancellationToken) =>
        WithCommittedIntent(request, (connection, intent) =>
        {
            if (intent.State != HostTaskState.IntentRecorded || intent.Revision.Value != 1
                || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            {
                throw new InvalidOperationException("Lifecycle control requires a fresh original-user control intent.");
            }
            return operation(connection, intent);
        }, cancellationToken, requireIdle: true);

    public ValueTask<SessionPage<HostTaskRecord>> ReadSessionPageAsync(HostId<SessionIdentity> session,
        Guid? after, int limit, CancellationToken cancellationToken)
    {
        session.Validate();
        ValidatePage(after, limit);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = OpenDatabase(created: false, cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM host_tasks WHERE session_id=$session AND task_id>$after ORDER BY task_id LIMIT $limit;";
            command.Parameters.AddWithValue("$session", session.Value.ToString("D"));
            command.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
            command.Parameters.AddWithValue("$limit", limit + 1);
            using var reader = command.ExecuteReader();
            var rows = new List<HostTaskRecord>();
            while (reader.Read()) { rows.Add(Decode(reader)); }
            database.VerifyFiles();
            return new SessionPage<HostTaskRecord>([.. rows.Take(limit)],
                rows.Count > limit ? rows[limit - 1].Request.TaskId.Value : null);
        }, cancellationToken));
    }

    internal static void ValidatePage(Guid? after, int limit)
    {
        if (after == Guid.Empty || limit is < 1 or > SessionPage<HostTaskRecord>.MaximumRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Use a nonempty cursor and a page limit between 1 and 50.");
        }
    }

    public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken) =>
        QueueCommit(record, expectedRevision, requireExisting: false, cancellationToken);

    internal ValueTask CommitControlIntentAsync(HostTaskRecord record, CancellationToken cancellationToken) =>
        QueueCommit(record, expectedRevision: 0, requireExisting: true, cancellationToken);

    private ValueTask QueueCommit(HostTaskRecord record, long expectedRevision, bool requireExisting,
        CancellationToken cancellationToken)
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
        return new ValueTask(Task.Run(() => Commit(record, expectedRevision, requireExisting, cancellationToken), cancellationToken));
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

    private void Commit(HostTaskRecord record, long expectedRevision, bool requireExisting, CancellationToken cancellationToken)
    {
        var live = HostActivity.RequireCurrent();
        if (live.Activity!.IsStopped || live.Request != record.Request)
        {
            throw new InvalidOperationException("The queued storage operation lost its live host request.");
        }
        using var boundary = new StorageOperation("storage.task.commit", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var created = false;
        using var lease = requireExisting || validateAuthority is not null ? database.AcquireReadLease(cancellationToken)
            : database.AcquireLease(out created, cancellationToken);
        using var connection = OpenDatabase(created, cancellationToken);
        using var transaction = connection.BeginTransaction();
        checkpoint?.BeforeWrite(connection, transaction);
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
        if (validateAuthority is not null)
        {
            RequireNotDisposed(connection, record.Request.SessionId);
        }
        if (validateAuthority is not null && record.State is HostTaskState.DispatchRecorded or HostTaskState.Cancelled)
        {
            using var waiting = connection.CreateCommand();
            waiting.Transaction = transaction;
            waiting.CommandText = "SELECT 1 FROM host_task_waits WHERE task_id=$id;";
            waiting.Parameters.AddWithValue("$id", record.Request.TaskId.Value.ToString("D"));
            if (waiting.ExecuteScalar() is not null)
            {
                throw new InvalidOperationException("Admitted question waits require the shared atomic cancellation or answered pre-dispatch gateway.");
            }
        }

        WriteTask(connection, transaction, record);
        if (expectedRevision == 0 && authorityRun is { } run)
        {
            using var source = connection.CreateCommand();
            source.Transaction = transaction;
            source.CommandText = "INSERT INTO host_task_runs VALUES($task,$run);";
            source.Parameters.AddWithValue("$task", record.Request.TaskId.Value.ToString("D"));
            source.Parameters.AddWithValue("$run", run.ToString("D"));
            source.ExecuteNonQuery();
        }
        checkpoint?.BeforeCommit(connection, transaction);
        database.VerifyFiles();
        cancellationToken.ThrowIfCancellationRequested();
        // Once COMMIT succeeds, cancellation must not turn a durable receipt into a cancelled result.
        transaction.Commit();
        boundary.Complete();
    }

    internal static void WriteTask(SqliteConnection connection, SqliteTransaction transaction, HostTaskRecord record)
    {
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
        using var schema = connection.CreateCommand();
        schema.Transaction = transaction;
        schema.CommandText = "SELECT 1 FROM sqlite_schema WHERE name='session_history';";
        if (schema.ExecuteScalar() is not null)
        {
            SessionHistoryPersistence.Task(connection, transaction, record);
        }
    }

    private void RequireNotDisposed(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        if (validateAuthority is null) { return; }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM work_sessions WHERE session_id=$id AND state=2;";
        command.Parameters.AddWithValue("$id", session.Value.ToString("D"));
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidOperationException("A disposed session cannot admit new interaction work.");
        }
    }

    private ReadOnlyCollection<HostTaskRecord> ReadIncomplete(int limit, CancellationToken cancellationToken)
    {
        using var boundary = new StorageOperation("storage.task.read", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = AcquireTaskLease(out var created, cancellationToken);
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

    private SqliteConnection OpenDatabase(bool created, CancellationToken cancellationToken, bool allowFrozen = false)
    {
        var connection = database.Open(created, cancellationToken);
        try
        {
            if (created && ReferenceEquals(database, legacy))
            {
                using var initial = connection.CreateCommand();
                initial.CommandText = "INSERT INTO task_authority_handoff VALUES(1,0);";
                initial.ExecuteNonQuery();
            }

            ValidateTasks(connection);
            validateAuthority?.Invoke(connection);
            if (ReferenceEquals(database, legacy))
            {
                using var handoff = connection.CreateCommand();
                handoff.CommandText = "SELECT frozen FROM task_authority_handoff WHERE singleton=1;";
                if (handoff.ExecuteScalar() is not long value || (!allowFrozen && value != 0))
                {
                    throw new InvalidOperationException("Legacy task authority is frozen or missing. Open the consolidated host store; no work was replayed.");
                }
            }
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private FileStream AcquireTaskLease(out bool created, CancellationToken token)
    {
        created = false;
        return validateAuthority is null ? database.AcquireLease(out created, token) : database.AcquireReadLease(token);
    }

    internal static void ValidateTasks(SqliteConnection connection)
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

    internal static HostTaskRecord Decode(SqliteDataReader reader)
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
