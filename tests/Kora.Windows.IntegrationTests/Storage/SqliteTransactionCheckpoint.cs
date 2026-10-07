using System.Runtime.InteropServices;

using AwesomeAssertions;

using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

internal sealed partial class SqliteTransactionCheckpoint : ISqliteTransactionCheckpoint
{
    internal Action<SqliteConnection, SqliteTransaction>? Write { get; init; }
    internal Action<SqliteConnection, SqliteTransaction>? Commit { get; init; }

    public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) => Write?.Invoke(connection, transaction);
    public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) => Commit?.Invoke(connection, transaction);

    internal static void SpillPages(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA cache_size=1; PRAGMA cache_spill=ON;";
        command.ExecuteNonQuery();
    }

    internal static void FlushPages(SqliteConnection connection)
    {
        // Force an actual pre-COMMIT pager write, including transactions that only extend the file.
        var handle = connection.Handle ?? throw new InvalidOperationException("The fixture connection is not open.");
        FlushDatabaseCache(handle.DangerousGetHandle()).Should().Be(SQLitePCL.raw.SQLITE_OK);
    }

    [LibraryImport("e_sqlite3", EntryPoint = "sqlite3_db_cacheflush")]
    private static partial int FlushDatabaseCache(IntPtr database);
}
