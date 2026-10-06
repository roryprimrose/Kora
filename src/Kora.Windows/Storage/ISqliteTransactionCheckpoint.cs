using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

// Internal fault/interruption seam; production construction supplies no observer.
internal interface ISqliteTransactionCheckpoint
{
    void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction);
    void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction);
}
