using System.Diagnostics;
using System.Globalization;

using Kora.Core.Dependencies;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

internal sealed class RestrictedSqliteDatabase
{
    private readonly RestrictedStorageDirectory directory;
    private readonly string databasePath;
    private readonly string journalPath;
    private readonly int applicationId;
    private readonly IReadOnlyList<string> schema;
    private readonly RestrictedSqliteMigration? migration;

    internal RestrictedSqliteDatabase(IApplicationDataPaths paths, string partition, string fileName,
        int applicationId, IReadOnlyList<string> schema, RestrictedSqliteMigration? migration = null)
    {
        directory = new RestrictedStorageDirectory(paths, includeKeys: false, partitionName: partition);
        databasePath = Path.Combine(directory.Root, fileName);
        journalPath = string.Concat(databasePath, "-journal");
        this.applicationId = applicationId;
        this.schema = schema;
        this.migration = migration;
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

    internal SqliteConnection OpenReadOnly(CancellationToken cancellationToken)
    {
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
            var started = Stopwatch.GetTimestamp();
            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1000,
                _ => cancellationToken.IsCancellationRequested
                    || Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5) ? 1 : 0, null);
            using var settings = connection.CreateCommand();
            settings.CommandText = "PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";
            settings.ExecuteNonQuery();
            ValidateSchema(connection);
            ValidateIntegrity(connection);
            VerifyFiles();
            cancellationToken.ThrowIfCancellationRequested();
            return connection;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidDataException("The private evidence database could not be read or validated.", exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal SqliteConnection Open(bool created, CancellationToken cancellationToken = default)
    {
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
            connection.Open();
            var started = Stopwatch.GetTimestamp();
            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1000,
                _ => cancellationToken.IsCancellationRequested
                    || Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5) ? 1 : 0, null);
            using var settings = connection.CreateCommand();
            // Exclusive mode must precede any page read: hot-journal recovery otherwise unlinks the
            // preowned journal before the connection has had a chance to select PERSIST.
            settings.CommandText = "PRAGMA locking_mode=EXCLUSIVE; PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;";
            settings.ExecuteNonQuery();
            VerifyFiles();
            if (!created)
            {
                Migrate(connection, cancellationToken);
                ValidateSchema(connection);
            }
            if (created)
            {
                using var transaction = connection.BeginTransaction();
                using var create = connection.CreateCommand();
                create.Transaction = transaction;
                create.CommandText = string.Join(";\n", schema) + $"; PRAGMA application_id={applicationId}; PRAGMA user_version={migration?.ToVersion ?? 1};";
                create.ExecuteNonQuery();
                VerifyFiles();
                transaction.Commit();
                ValidateSchema(connection);
            }
            ValidateIntegrity(connection);
            return connection;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidDataException("The private SQLite database could not be opened or validated.", exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ValidateIntegrity(SqliteConnection connection)
    {
        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA quick_check;";
        using var reader = integrity.ExecuteReader();
        if (!reader.Read() || !string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal) || reader.Read())
        {
            throw new InvalidDataException("The private SQLite database failed its integrity check.");
        }
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

    private void Migrate(SqliteConnection connection, CancellationToken token)
    {
        if (migration is null) { return; }
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != migration.FromVersion) { return; }
        ValidateSchema(connection, migration.FromVersion, migration.PreviousSchema);
        ValidateIntegrity(connection);
        using var transaction = connection.BeginTransaction();
        migration.Apply(connection, transaction);
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA user_version={migration.ToVersion};";
        command.ExecuteNonQuery();
        ValidateSchema(connection);
        VerifyFiles();
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private void ValidateSchema(SqliteConnection connection, int? version = null, IReadOnlyList<string>? expectedSchema = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA application_id;";
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != applicationId)
        {
            throw new InvalidDataException("The private database identity is invalid.");
        }
        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != (version ?? migration?.ToVersion ?? 1))
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
