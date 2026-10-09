using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

internal sealed record RestrictedSqliteMigration(
    int FromVersion, int ToVersion, IReadOnlyList<string> PreviousSchema,
    Action<SqliteConnection, SqliteTransaction, CancellationToken> Apply);
