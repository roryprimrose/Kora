using System.Globalization;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

internal sealed partial class RestrictedSqliteDatabase
{
    internal static readonly TimeSpan DefaultProgressTimeout = TimeSpan.FromSeconds(5);
    private readonly RestrictedStorageDirectory directory;
    private readonly string databasePath;
    private readonly string journalPath;
    private readonly int applicationId;
    private readonly IReadOnlyList<string> schema;
    private readonly RestrictedSqliteMigration? migration;
    private readonly RestrictedSqliteMigration? continuation;
    private readonly RestrictedSqliteMigration? finalMigration;
    private readonly RestrictedSqliteMigration? latestMigration;
    private readonly RestrictedSqliteMigration? retentionMigration;
    private readonly RestrictedSqliteMigration? memoryMigration;
    private readonly RestrictedSqliteMigration? attachmentMigration;
    private readonly RestrictedSqliteMigration? providerProfileMigration;
    private readonly RestrictedSqliteMigration? queueOrderMigration;
    private readonly RestrictedSqliteMigration? conversationMigration;
    private readonly RestrictedSqliteMigration? conversationExecutionMigration;
    private readonly RestrictedSqliteMigration? sessionPermissionMigration;
    private readonly Action<SqliteConnection>? migrationAdmission;
    private readonly int version;

    internal RestrictedSqliteDatabase(IApplicationDataPaths paths, string partition, string fileName,
        int applicationId, IReadOnlyList<string> schema, RestrictedSqliteMigration? migration = null,
        RestrictedSqliteMigration? continuation = null, RestrictedSqliteMigration? finalMigration = null,
        RestrictedSqliteMigration? latestMigration = null, RestrictedSqliteMigration? retentionMigration = null,
        int? currentVersion = null, RestrictedSqliteMigration? memoryMigration = null,
        RestrictedSqliteMigration? attachmentMigration = null, Action<SqliteConnection>? migrationAdmission = null,
        RestrictedSqliteMigration? providerProfileMigration = null,
        RestrictedSqliteMigration? queueOrderMigration = null,
        RestrictedSqliteMigration? conversationMigration = null,
        RestrictedSqliteMigration? conversationExecutionMigration = null,
        RestrictedSqliteMigration? sessionPermissionMigration = null)
    {
        directory = new RestrictedStorageDirectory(paths, includeKeys: false, partitionName: partition);
        databasePath = Path.Combine(directory.Root, fileName);
        journalPath = string.Concat(databasePath, "-journal");
        this.applicationId = applicationId;
        this.schema = schema;
        this.migration = migration;
        this.continuation = continuation;
        this.finalMigration = finalMigration;
        this.latestMigration = latestMigration;
        this.retentionMigration = retentionMigration;
        this.memoryMigration = memoryMigration;
        this.attachmentMigration = attachmentMigration;
        this.providerProfileMigration = providerProfileMigration;
        this.queueOrderMigration = queueOrderMigration;
        this.conversationMigration = conversationMigration;
        this.conversationExecutionMigration = conversationExecutionMigration;
        this.sessionPermissionMigration = sessionPermissionMigration;
        this.migrationAdmission = migrationAdmission;
        version = currentVersion ?? sessionPermissionMigration?.ToVersion ?? conversationExecutionMigration?.ToVersion ?? conversationMigration?.ToVersion ?? queueOrderMigration?.ToVersion ?? providerProfileMigration?.ToVersion ?? attachmentMigration?.ToVersion ?? memoryMigration?.ToVersion ?? retentionMigration?.ToVersion ?? latestMigration?.ToVersion ?? finalMigration?.ToVersion
            ?? continuation?.ToVersion ?? migration?.ToVersion ?? 1;
    }

    internal FileStream AcquireLease(out bool created, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        created = !Directory.Exists(directory.Root);
        if (created)
        {
            directory.CreateNew();
        }
        return directory.AcquireBoundedLease(requireExisting: !created, cancellationToken);
    }

    internal FileStream AcquireReadLease(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!directory.HasExistingPartition())
        {
            throw new FileNotFoundException("The private evidence partition is unavailable. No replacement was created.");
        }

        return directory.AcquireBoundedLease(requireExisting: true, cancellationToken);
    }

    internal bool HasExistingPartition() => directory.HasExistingPartition();

    internal void RequireEmptyArtifactInventory()
    {
        directory.Verify();
        if (Directory.EnumerateFileSystemEntries(directory.Artifacts).Any())
        {
            throw new InvalidDataException("Unregistered artifacts, staging or managed backups hold retention; ownership must be inventoried before removal.");
        }
    }

    internal void ClearCommittedJournal()
    {
        VerifyFiles();
        using (var journal = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Span<byte> header = stackalloc byte[8];
            if (journal.Length != 0)
            {
                if (journal.Length < header.Length) { throw new InvalidDataException("A committed journal has an uncertain header."); }
                journal.ReadExactly(header);
                if (header.ContainsAnyExcept((byte)0))
                {
                    throw new InvalidDataException("A hot or uncertain journal cannot be discarded by retention.");
                }
                journal.SetLength(0);
                journal.Flush(flushToDisk: true);
            }
        }
        RequireEmptyJournal();
    }

    internal void RequireEmptyJournal()
    {
        VerifyFiles();
        if (new FileInfo(journalPath).Length != 0)
        {
            throw new IOException("Content deletion committed but the persistent rollback journal was not cleared; deletion acceptance is held.");
        }
    }

    internal SqliteConnection OpenReadOnly(CancellationToken cancellationToken) =>
        OpenReadOnly(DefaultProgressTimeout, timeProvider: null, cancellationToken);

    internal SqliteConnection OpenReadOnly(TimeSpan integrityTimeout, TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(integrityTimeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        VerifyFiles();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            Cache = SqliteCacheMode.Private, DefaultTimeout = 5,
        }.ToString());
        try
        {
            connection.Open();
            ResetProgressBudget(connection, cancellationToken);
            using var settings = connection.CreateCommand();
            settings.CommandText = "PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";
            settings.ExecuteNonQuery();
            ValidateSchema(connection, cancellationToken: cancellationToken);
            ValidateIntegrity(connection, integrityTimeout, timeProvider, cancellationToken);
            VerifyFiles();
            cancellationToken.ThrowIfCancellationRequested();
            return connection;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            if (exception.SqliteErrorCode == SQLitePCL.raw.SQLITE_INTERRUPT)
            {
                throw new IOException("The private evidence database read or validation was interrupted before completion.", exception);
            }
            throw new InvalidDataException("The private evidence database could not be read or validated.", exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal SqliteConnection Open(bool created, CancellationToken cancellationToken = default) =>
        Open(created, DefaultProgressTimeout, cancellationToken);

    internal SqliteConnection Open(bool created, TimeSpan integrityTimeout, CancellationToken cancellationToken = default) =>
        OpenCore(created, integrityTimeout, timeRendererAudit: false, cancellationToken);

    internal SqliteConnection OpenForRendererAudit(bool created, TimeSpan? integrityTimeout = null) =>
        OpenCore(created, integrityTimeout ?? DefaultProgressTimeout, timeRendererAudit: true, CancellationToken.None);

    private SqliteConnection OpenCore(bool created, TimeSpan integrityTimeout, bool timeRendererAudit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(integrityTimeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        if (created)
        {
            using (var database = directory.CreateNewFile(databasePath))
            {
                database.Flush(flushToDisk: true);
            }
            using var journal = directory.CreateNewFile(journalPath);
            journal.Flush(flushToDisk: true);
        }
        VerifyFiles();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 5,
        }.ToString());
        try
        {
            if (timeRendererAudit) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.SqliteAuditConnectionStarted); }
            connection.Open();
            if (timeRendererAudit) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.SqliteAuditConnectionReturned); }
            ResetProgressBudget(connection, cancellationToken);
            using var settings = connection.CreateCommand();
            // Exclusive mode must precede any page read: hot-journal recovery otherwise unlinks the
            // preowned journal before the connection has had a chance to select PERSIST.
            settings.CommandText = "PRAGMA locking_mode=EXCLUSIVE; PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;";
            settings.ExecuteNonQuery();
            VerifyFiles();
            if (!created)
            {
                if (timeRendererAudit) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.SqliteAuditSchemaStarted); }
                Migrate(connection, cancellationToken);
                ValidateSchema(connection, cancellationToken: cancellationToken);
                if (timeRendererAudit) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.SqliteAuditSchemaReturned); }
            }
            if (created)
            {
                using var transaction = connection.BeginTransaction();
                using var create = connection.CreateCommand();
                create.Transaction = transaction;
                create.CommandText = string.Join(";\n", schema) + $"; PRAGMA application_id={applicationId}; PRAGMA user_version={version};";
                create.ExecuteNonQuery();
                VerifyFiles();
                transaction.Commit();
                ValidateSchema(connection, cancellationToken: cancellationToken);
            }
            if (timeRendererAudit) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.SqliteAuditIntegrityStarted); }
            ValidateIntegrity(connection, integrityTimeout, cancellationToken: cancellationToken);
            if (timeRendererAudit) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.SqliteAuditIntegrityReturned); }
            return connection;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            if (exception.SqliteErrorCode == SQLitePCL.raw.SQLITE_INTERRUPT)
            {
                throw new IOException("The private SQLite database open or validation was interrupted before completion.", exception);
            }
            throw new InvalidDataException("The private SQLite database could not be opened or validated.", exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static void ValidateIntegrity(SqliteConnection connection, CancellationToken cancellationToken = default) =>
        ValidateIntegrity(connection, DefaultProgressTimeout, cancellationToken: cancellationToken);

    internal static void ValidateIntegrity(SqliteConnection connection, TimeSpan timeout, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetProgressBudget(connection, timeout, timeProvider, cancellationToken);
        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA quick_check;";
        using var reader = integrity.ExecuteReader();
        if (!reader.Read() || !string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal) || reader.Read())
        {
            throw new InvalidDataException("The private SQLite database failed its integrity check.");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void VerifyFiles()
    {
        directory.Verify();
        if (!File.Exists(databasePath) || !File.Exists(journalPath))
        {
            throw new InvalidDataException("An existing private database or persistent journal is missing; replacement is forbidden.");
        }
        foreach (var path in Directory.EnumerateFiles(directory.Root))
        {
            directory.VerifyFile(path);
            if (!string.Equals(path, databasePath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, journalPath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "operation.lock", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("An unexpected file exists in the private SQLite partition.");
            }
        }
    }

    internal static void ResetProgressBudget(SqliteConnection connection,
        CancellationToken cancellationToken = default) =>
        ResetProgressBudget(connection, TimeProvider.System, cancellationToken);

    internal static void ResetProgressBudget(SqliteConnection connection,
        TimeProvider? timeProvider, CancellationToken cancellationToken = default) =>
        ResetProgressBudget(connection, DefaultProgressTimeout, timeProvider, cancellationToken);

    internal static void ResetProgressBudget(SqliteConnection connection, TimeSpan timeout,
        TimeProvider? timeProvider, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1000,
            _ => cancellationToken.IsCancellationRequested
                || clock.GetElapsedTime(started) >= timeout ? 1 : 0, null);
    }

    private void Migrate(SqliteConnection connection, CancellationToken token)
    {
        migrationAdmission?.Invoke(connection);
        ApplyMigration(connection, migration, continuation?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, continuation, finalMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, finalMigration, latestMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, latestMigration, retentionMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, retentionMigration, memoryMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, memoryMigration, attachmentMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, attachmentMigration, providerProfileMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, providerProfileMigration, queueOrderMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, queueOrderMigration, conversationMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, conversationMigration, conversationExecutionMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, conversationExecutionMigration, sessionPermissionMigration?.PreviousSchema ?? schema, token);
        ApplyMigration(connection, sessionPermissionMigration, schema, token);
    }

    private void ApplyMigration(SqliteConnection connection, RestrictedSqliteMigration? step,
        IReadOnlyList<string> nextSchema, CancellationToken token)
    {
        if (step is null) { return; }
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != step.FromVersion) { return; }
        ValidateSchema(connection, step.FromVersion, step.PreviousSchema, token);
        ValidateIntegrity(connection, token);
        using var transaction = connection.BeginTransaction();
        step.Apply(connection, transaction, token);
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA user_version={step.ToVersion};";
        command.ExecuteNonQuery();
        ValidateSchema(connection, step.ToVersion, nextSchema, token);
        VerifyFiles();
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private void ValidateSchema(SqliteConnection connection, int? version = null, IReadOnlyList<string>? expectedSchema = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetProgressBudget(connection, cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA application_id;";
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != applicationId)
        {
            throw new InvalidDataException("The private database identity is invalid.");
        }
        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != (version ?? this.version))
        {
            throw new InvalidDataException("The private database schema version is unsupported.");
        }
        command.CommandText = "SELECT sql FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var actual = new List<string>();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                throw new InvalidDataException("The private database has an unrecognized schema object.");
            }
            actual.Add(Normalize(reader.GetString(0)));
        }
        var expected = (expectedSchema ?? schema).Select(Normalize).Order(StringComparer.Ordinal).ToArray();
        if (!actual.Order(StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The private database tables, constraints, indexes or triggers are invalid.");
        }
    }

    private static string Normalize(string sql) =>
        string.Join(' ', sql.Trim().TrimEnd(';').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
