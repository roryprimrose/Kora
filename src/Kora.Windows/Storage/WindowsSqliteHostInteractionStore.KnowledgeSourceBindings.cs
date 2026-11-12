using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionKnowledgeSourceBindingStore
{
    private void MigrateKnowledgeSourceBindings(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateAuthority(connection);
        using var prior = connection.CreateCommand();
        prior.Transaction = transaction;
        prior.CommandText = """
            SELECT 1 FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='session-source' LIMIT 1;
            """;
        if (prior.ExecuteScalar() is not null)
        { throw new InvalidDataException("A downgraded schema cannot replace committed source associations."); }
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, HostInteractionSchema.KnowledgeSourceBindingTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<SessionKnowledgeSourceBinding?> ReadBinding(HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(token);
            ValidateAuthority(connection);
            RequireAttachmentReadable(connection, session);
            var binding = ReadKnowledgeSourceBindingRow(connection, session)?.Binding;
            token.ThrowIfCancellationRequested();
            database.VerifyFiles();
            if (!Same(identity, RequireInspectionIdentity()))
            { throw new InvalidDataException("Source association storage changed during inspection."); }
            return binding;
        }, token));
    }

    public async ValueTask<SessionKnowledgeSourceBinding> Bind(HostRequest request, HostRevision generation,
        KnowledgeSourceReference source, long sourceVersion, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(admitted);
        var binding = await RunHostMutationAsync(request, "session.source.bind", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            RequireOriginalBindingInput(request);
            _ = RequireInspectionIdentity();
            RequireAttachmentReadable(connection, request.SessionId);
            var owner = RequireSession(connection, request.SessionId).Authority;
            if (!owner.IsActive || owner.Generation != generation || !admitted())
            { throw new InvalidOperationException("Binding requires the exact reviewed active conversation generation."); }
            var previous = ReadKnowledgeSourceBindingRow(connection, request.SessionId);
            if (previous?.Binding is not null)
            { throw new InvalidOperationException("Detach the exact existing association before reviewing another source."); }
            var result = new SessionKnowledgeSourceBinding(Guid.NewGuid(), ReadMemoryProfile(connection).Value,
                request.SessionId, generation, new(checked((previous?.Revision ?? 0) + 1)), source, sourceVersion);
            var payload = HostInteractionCodec.Encode(result);
            var completed = intent.Next(HostTaskState.Succeeded);
            var sequence = AppendAudit(connection, transaction, intent, owner, audit,
                changes: [KnowledgeSourceBindingChange(request.SessionId, result.StorageRevision.Value, payload), TaskChange(completed)]);
            WriteKnowledgeSourceBinding(connection, transaction, request.SessionId, result.StorageRevision.Value, payload, sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            return result;
        }, token, admitted).ConfigureAwait(false);
        var observed = await ReadBinding(request.SessionId, CancellationToken.None).ConfigureAwait(false);
        if (!admitted() || !Equals(observed, binding))
        { throw new IOException("Association committed but exact source/admission readback is unconfirmed; inspect durable state before retrying."); }
        return binding;
    }

    public async ValueTask Detach(HostRequest request, SessionKnowledgeSourceBinding exact, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(exact);
        ArgumentNullException.ThrowIfNull(admitted);
        await RunHostMutationAsync(request, "session.source.detach", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            RequireOriginalBindingInput(request);
            RequireAttachmentReadable(connection, request.SessionId);
            if (request.SessionId != exact.Session || !Equals(ReadKnowledgeSourceBindingRow(connection, request.SessionId)?.Binding, exact))
            { throw new InvalidOperationException("Only the exact reviewed association may be detached."); }
            var completed = intent.Next(HostTaskState.Succeeded);
            var revision = checked(exact.StorageRevision.Value + 1);
            var sequence = AppendAudit(connection, transaction, intent, RequireSession(connection, request.SessionId).Authority,
                audit, changes: [KnowledgeSourceBindingChange(exact.Session, revision, "null"), TaskChange(completed)]);
            WriteKnowledgeSourceBinding(connection, transaction, exact.Session, revision, "null", sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            return true;
        }, token, admitted).ConfigureAwait(false);
        if (!admitted() || await ReadBinding(request.SessionId, CancellationToken.None).ConfigureAwait(false) is not null)
        { throw new IOException("Detach committed but exact readback is unconfirmed; inspect durable state before retrying."); }
    }

    private static void RequireOriginalBindingInput(HostRequest request)
    {
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null)
        { throw new InvalidOperationException("Source associations require separate original-user native confirmation."); }
    }

    private sealed record KnowledgeSourceBindingRow(long Revision, SessionKnowledgeSourceBinding? Binding);

    private static KnowledgeSourceBindingRow? ReadKnowledgeSourceBindingRow(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision,payload,audit_sequence FROM session_knowledge_source WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return null; }
        var revision = reader.GetInt64(0);
        var payload = reader.GetString(1);
        var binding = Same(payload, "null") ? null : HostInteractionCodec.Decode<SessionKnowledgeSourceBinding>(payload);
        if (revision <= 0 || binding is not null && (binding.Session != session
            || binding.ProfileId != ReadMemoryProfile(connection).Value || binding.StorageRevision.Value != revision
            || binding.Generation.Value > RequireSession(connection, session).Authority.Generation.Value
            || RequireSession(connection, session).State == 2 || !Same(payload, HostInteractionCodec.Encode(binding))))
        { throw new InvalidDataException("The source association profile, session or revision is invalid."); }
        ValidateRowAuthority(connection, reader.GetInt64(2), KnowledgeSourceBindingChange(session, revision, payload));
        return new(revision, binding);
    }

    private static AuthorityChange KnowledgeSourceBindingChange(HostId<SessionIdentity> session, long revision, string payload) =>
        Change("session-source", Id(session), revision, payload);

    private static void WriteKnowledgeSourceBinding(SqliteConnection connection, SqliteTransaction transaction,
        HostId<SessionIdentity> session, long revision, string payload, long sequence) =>
        Execute(connection, transaction, """
            INSERT INTO session_knowledge_source VALUES($id,$revision,$payload,$audit)
            ON CONFLICT(session_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
            """, ("$id", Id(session)), ("$revision", revision), ("$payload", payload), ("$audit", sequence));

    private static AuthorityChange[] KnowledgeSourceBindingInvalidation(SqliteConnection connection, HostId<SessionIdentity> session) =>
        ReadKnowledgeSourceBindingRow(connection, session) is { Binding: not null } row
            ? [KnowledgeSourceBindingChange(session, checked(row.Revision + 1), "null")] : [];

    private static void RevokeKnowledgeSourceBinding(SqliteConnection connection, SqliteTransaction transaction,
        HostId<SessionIdentity> session, long sequence)
    {
        if (ReadKnowledgeSourceBindingRow(connection, session) is { Binding: not null } row)
        { WriteKnowledgeSourceBinding(connection, transaction, session, checked(row.Revision + 1), "null", sequence); }
    }

    private static void ValidateKnowledgeSourceBindings(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id FROM session_knowledge_source;";
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) { _ = ReadKnowledgeSourceBindingRow(connection, new(ParseId(rows.GetString(0)))); }
        }
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') id,MAX(a.sequence) sequence
                FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='session-source' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN session_knowledge_source b ON b.session_id=latest.id
            WHERE b.audit_sequence IS NULL OR b.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        { throw new InvalidDataException("Committed source association or detach provenance is missing or stale."); }
    }
}
