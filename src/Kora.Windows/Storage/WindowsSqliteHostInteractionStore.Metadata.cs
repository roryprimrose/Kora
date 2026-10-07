using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session,
        CancellationToken cancellationToken) => new(Task.Run(() =>
    {
        using var lease = database.AcquireReadLease(cancellationToken);
        using var connection = Open(created: false, cancellationToken);
        var record = RequireSession(connection, session);
        if (record.State == 2) { throw new InvalidOperationException("Removed sessions are not browsable."); }
        var entry = new SessionWorkspaceEntry(record.Authority, ReadMetadata(connection, session));
        database.VerifyFiles();
        return entry;
    }, cancellationToken));

    private void MigrateMetadata(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateAudit(connection);
        ValidateRows(connection);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='metadata' LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("A legacy schema cannot contain newer committed metadata. Downgrade or data loss is refused.");
        }
        Execute(connection, transaction, HostInteractionSchema.MetadataTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        WindowsSqliteHostTaskStore.ValidatePage(after, limit);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT session_id,generation,state FROM work_sessions
                WHERE state<>2 AND session_id>$after ORDER BY session_id LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
            command.Parameters.AddWithValue("$limit", limit + 1);
            using var reader = command.ExecuteReader();
            var rows = new List<SessionWorkspaceEntry>();
            while (reader.Read())
            {
                var authority = DecodeSession(reader).Authority;
                rows.Add(new(authority, ReadMetadata(connection, authority.SessionId)));
            }
            database.VerifyFiles();
            return new SessionPage<SessionWorkspaceEntry>([.. rows.Take(limit)],
                rows.Count > limit ? rows[limit - 1].Authority.SessionId.Value : null);
        }, cancellationToken));
    }

    public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
        Func<bool> canControl, CancellationToken cancellationToken)
    {
        RequireMetadataControl(request, name, canControl);
        return RunHostMutationAsync(request, "session.create.named", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            if (ReadSession(connection, request.SessionId) is not null)
            {
                throw new InvalidOperationException("The durable work-session identity already exists.");
            }
            var authority = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var metadata = new SessionMetadata(request.SessionId, new(1), name);
            var sequence = AppendAudit(connection, transaction, intent, authority, audit,
                changes: [SessionChange(authority, 0), MetadataChange(metadata)]);
            WriteSession(connection, transaction, authority, 0, sequence);
            WriteMetadata(connection, transaction, metadata, sequence);
            return new SessionWorkspaceEntry(authority, metadata);
        }, cancellationToken, canControl);
    }

    public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration,
        long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken)
    {
        RequireMetadataControl(request, name, canControl);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedMetadataRevision);
        return RunHostMutationAsync(request, "session.rename", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            var session = RequireSession(connection, request.SessionId);
            var previous = ReadMetadata(connection, request.SessionId);
            if (session.State == 2 || session.Authority.Generation != expectedGeneration
                || (previous?.Revision.Value ?? 0) != expectedMetadataRevision)
            {
                throw new InvalidOperationException("The exact session generation or metadata revision conflicts. Refresh before retrying.");
            }
            var metadata = new SessionMetadata(request.SessionId, new(checked(expectedMetadataRevision + 1)), name);
            var sequence = AppendAudit(connection, transaction, intent, session.Authority, audit,
                changes: [MetadataChange(metadata)]);
            WriteMetadata(connection, transaction, metadata, sequence);
            return new SessionWorkspaceEntry(session.Authority, metadata);
        }, cancellationToken, canControl, requireIdle: false);
    }

    private static void RequireMetadataControl(HostRequest request, SessionName name, Func<bool> canControl)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(canControl);
        if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || !canControl())
        {
            throw new InvalidOperationException("Session metadata requires original trusted user input and current host admission.");
        }
    }

    private static void RequireFreshMetadataIntent(HostTaskRecord intent)
    {
        if (intent.State != HostTaskState.IntentRecorded || intent.Revision.Value != 1
            || intent.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Session metadata requires a fresh original-user control intent.");
        }
    }

    private static SessionMetadata? ReadMetadata(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id,revision,name,audit_sequence FROM session_metadata WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session));
        using var reader = command.ExecuteReader();
        return reader.Read() ? DecodeMetadata(connection, reader) : null;
    }

    private static SessionMetadata DecodeMetadata(SqliteConnection connection, SqliteDataReader reader)
    {
        var metadata = new SessionMetadata(new(ParseId(reader.GetString(0))), new(reader.GetInt64(1)), new(reader.GetString(2)));
        ValidateRowAuthority(connection, reader.GetInt64(3), MetadataChange(metadata));
        return metadata;
    }

    private static void ValidateMetadata(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id,revision,name,audit_sequence FROM session_metadata;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) { _ = DecodeMetadata(connection, reader); }
        }
        // Legacy absence is valid only when no metadata commit ever existed for that identity.
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id, MAX(a.sequence) AS sequence
                FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='metadata' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN session_metadata m ON m.session_id=latest.id
            WHERE m.audit_sequence IS NULL OR m.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Committed session metadata is missing; replacement is forbidden.");
        }
    }

    private static AuthorityChange MetadataChange(SessionMetadata metadata) =>
        Change("metadata", Id(metadata.SessionId), metadata.Revision.Value, HostInteractionCodec.Encode(metadata));

    private static void WriteMetadata(SqliteConnection connection, SqliteTransaction transaction,
        SessionMetadata metadata, long sequence)
    {
        var written = Execute(connection, transaction, """
            INSERT INTO session_metadata VALUES($id,$revision,$name,$audit)
            ON CONFLICT(session_id) DO UPDATE SET revision=excluded.revision,name=excluded.name,audit_sequence=excluded.audit_sequence
            WHERE session_metadata.revision+1=excluded.revision;
            """, ("$id", Id(metadata.SessionId)), ("$revision", metadata.Revision.Value),
            ("$name", metadata.Name.Value), ("$audit", sequence));
        if (written != 1)
        {
            throw new InvalidDataException("The durable metadata revision conflicts.");
        }
    }
}
