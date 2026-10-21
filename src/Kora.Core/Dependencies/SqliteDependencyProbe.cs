using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using Kora.Core.Diagnostics;

namespace Kora.Core.Dependencies;

public sealed class SqliteDependencyProbe(
    IApplicationDataPaths paths,
    ILogger<SqliteDependencyProbe> logger) : ISetupDependencyProbe
{
    public string TaskId => "kora.sqlite";

    public string TaskName => "Setup record storage";

    public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.LocalRoot);
        var databasePath = Path.Combine(paths.LocalRoot, "kora.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        DependencyStatus status;
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check";
                var result = (string?)await integrity.ExecuteScalarAsync(cancellationToken);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return new DependencyStatus(
                        "kora.sqlite",
                        "Setup record storage",
                        DependencyReadiness.Failed,
                        $"Setup record check failed: {result}. Existing records were not replaced; restore a known-good backup.");
                }
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version";
            var version = RequireSchemaVersion(await command.ExecuteScalarAsync(cancellationToken));
            if (version > 1)
            {
                return new DependencyStatus(
                    "kora.sqlite",
                    "Setup record storage",
                    DependencyReadiness.Incompatible,
                    "These setup records use a newer format than this version of Kora supports. Update Kora to open them. Existing data was not modified.");
            }

            if (version == 0)
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS setup_tasks (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL,
                        state TEXT NOT NULL,
                        detail TEXT NOT NULL,
                        updated_utc TEXT NOT NULL
                    );
                    PRAGMA user_version = 1;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            status = new DependencyStatus(
                "kora.sqlite",
                "Setup record storage",
                DependencyReadiness.Ready,
                "Setup records are ready. Saving content and verified activity history requires protected storage that is not yet available through this setup.");
        }
        catch (SqliteException exception)
        {
            CoreLog.SqliteStorageUnavailable(logger, exception, databasePath);
            status = new DependencyStatus(
                "kora.sqlite",
                "Setup record storage",
                DependencyReadiness.Failed,
                "Setup records could not be opened or updated. Existing data was not replaced. Check disk space and access to Kora's data folder, then try again.");
        }
        return status;
    }

    internal static long RequireSchemaVersion(object? result) =>
        (long)(result ?? throw new InvalidDataException("The database did not return a schema version."));
}
