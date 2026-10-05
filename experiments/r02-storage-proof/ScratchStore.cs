using Microsoft.Data.Sqlite;

namespace StorageProof;

// Private feasibility storage: opaque synthetic records, not future session/task contracts.
internal sealed class ScratchStore : IDisposable
{
    public SqliteConnection Connection { get; }
    private readonly byte[] key;
    private readonly bool pageCipher;

    public ScratchStore(string path, byte[] key, bool pageCipher, bool initialize = true)
    {
        this.key = key;
        this.pageCipher = pageCipher;
        Connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = initialize ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false,
            Password = pageCipher ? Convert.ToHexString(key) : null,
        }.ToString());
        try
        {
            Connection.Open();
            Execute("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA temp_store=MEMORY;");
            if (initialize)
            {
                Execute("""
                    PRAGMA journal_mode=WAL;
                    PRAGMA synchronous=FULL;
                    PRAGMA wal_autocheckpoint=0;
                    CREATE TABLE IF NOT EXISTS samples(id INTEGER PRIMARY KEY, payload BLOB NOT NULL, token BLOB NOT NULL);
                    CREATE INDEX IF NOT EXISTS token_lookup ON samples(token);
                    PRAGMA user_version=1;
                    """);
                if (pageCipher) Execute("CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(content);");
            }
        }
        catch
        {
            Connection.Dispose();
            throw;
        }
    }

    public void Append(long id, byte[] payload, SqliteTransaction? transaction = null)
    {
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO samples VALUES($id, $payload, $token)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$payload", pageCipher ? payload : Envelope.Seal(key, payload, "event:" + id));
        command.Parameters.AddWithValue("$token", Envelope.Token(key, "needle"));
        command.ExecuteNonQuery();
        if (pageCipher)
        {
            using var index = Connection.CreateCommand();
            index.Transaction = transaction;
            index.CommandText = "INSERT INTO search(rowid,content) VALUES($id,$content)";
            index.Parameters.AddWithValue("$id", id);
            index.Parameters.AddWithValue("$content", System.Text.Encoding.UTF8.GetString(payload));
            index.ExecuteNonQuery();
        }
    }

    public byte[] Read(long id)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT payload FROM samples WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        var value = (byte[])(command.ExecuteScalar() ?? throw new InvalidDataException("Synthetic row missing."));
        return pageCipher ? value : Envelope.Open(key, value, "event:" + id);
    }

    public long Count() => Convert.ToInt64(Scalar("SELECT count(*) FROM samples"));

    public object Scalar(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() ?? throw new InvalidDataException("Missing scalar result.");
    }

    public void Execute(string sql, SqliteTransaction? transaction = null)
    {
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Backup(string path)
    {
        // Microsoft.Data.Sqlite's BackupDatabase does not key the target for us.
        using var target = new ScratchStore(path, key, pageCipher);
        Connection.BackupDatabase(target.Connection);
    }

    public void Dispose() => Connection.Dispose();
}
