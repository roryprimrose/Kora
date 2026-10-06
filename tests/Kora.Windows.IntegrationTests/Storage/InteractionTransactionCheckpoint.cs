using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

internal sealed class InteractionTransactionCheckpoint : IHostInteractionTransactionCheckpoint
{
    internal Action<SqliteConnection, SqliteTransaction>? Audit { get; set; }
    internal Action<SqliteConnection, SqliteTransaction>? Commit { get; set; }

    public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) => Audit?.Invoke(connection, transaction);
    public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) => Commit?.Invoke(connection, transaction);
}
