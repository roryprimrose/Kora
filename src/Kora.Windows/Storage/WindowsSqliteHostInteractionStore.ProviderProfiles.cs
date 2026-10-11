using System.Collections.Concurrent;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionProviderProfileStore
{
    private const string ProviderProfileAction = "session.provider-profile";
    private readonly ConcurrentDictionary<HostId<SessionIdentity>, byte> heldProviderProfiles = new();

    private void MigrateProviderProfiles(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        ValidateQueue(connection);
        ValidateRetention(connection);
        ValidateMemory(connection);
        ValidateAttachments(connection);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='session-provider-profile'
                OR json_extract(a.envelope,'$.Audit.ActionId')='session.provider-profile' LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null) { throw new InvalidDataException("Downgraded provider policy authority cannot be recreated."); }
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, HostInteractionSchema.SessionProviderProfileTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<SessionProviderProfile> ReadProviderProfile(HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var row = ReadProviderProfileRow(connection, session);
            database.VerifyFiles();
            return row with { Held = row.Held || heldProviderProfiles.ContainsKey(session) };
        }, token));
    }

    public ValueTask<SessionProviderProfile> CommitProviderProfile(HostRequest request, SessionProviderProfile expected,
        SessionProviderOverrides desired, Func<bool> admitted, CancellationToken token)
    {
        RequireLive(request);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(admitted);
        return new(Task.Run(() =>
        {
            return tasks.WithCommittedIntent(request, (connection, intent) =>
                {
                    ValidateAuthority(connection);
                    RequireFreshMetadataIntent(intent);
                    var previous = ReadProviderProfileRow(connection, request.SessionId);
                    if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                        || request.InvocationId is not null || !previous.Session.IsActive || previous.Held
                        || heldProviderProfiles.ContainsKey(request.SessionId) || expected.Held
                        || previous.Session != expected.Session || previous.Revision != expected.Revision
                        || !Same(previous.Desired.Serialize(), expected.Desired.Serialize()) || !admitted())
                    { throw new InvalidOperationException("Refresh the exact active session, generation, policy and private original-user admission."); }
                    var requested = ProviderProfileAudit(request, SecurityAuditOutcome.Requested);
                    using var activity = HostActivity.BeginAudit(request, requested);
                    // An unresolved durable requested receipt holds even the old policy across restart.
                    heldProviderProfiles.TryAdd(request.SessionId, 0);
                    using (var preflight = connection.BeginTransaction())
                    {
                        AppendAudit(connection, preflight, intent, previous.Session, requested);
                        Commit(preflight, request, token, admitted);
                    }
                    var revision = checked(previous.Revision + 1);
                    var payload = desired.Serialize();
                    var completed = intent.Next(HostTaskState.Succeeded);
                    var writing = ProviderProfileAudit(request, SecurityAuditOutcome.Requested);
                    using (var saved = HostActivity.BeginAudit(request, writing))
                    using (var transaction = connection.BeginTransaction())
                    {
                        var sequence = AppendAudit(connection, transaction, intent, previous.Session, writing,
                            changes: [ProviderProfileChange(request.SessionId, previous.Session.Generation, revision, payload)]);
                        Execute(connection, transaction, """
                            INSERT INTO session_provider_profile VALUES($id,$generation,$revision,$payload,$audit)
                            ON CONFLICT(session_id) DO UPDATE SET generation=excluded.generation,revision=excluded.revision,
                                payload=excluded.payload,audit_sequence=excluded.audit_sequence;
                            """, ("$id", Id(request.SessionId)), ("$generation", previous.Session.Generation.Value),
                            ("$revision", revision), ("$payload", payload), ("$audit", sequence));
                        Commit(transaction, request, token, admitted);
                        saved.Complete(HostOperationOutcome.Completed);
                    }
                    database.VerifyFiles();
                    var readback = ReadProviderProfileRow(connection, request.SessionId);
                    if (readback.Revision != revision || !readback.Held || !Same(readback.Desired.Serialize(), payload))
                    { throw new InvalidDataException("Held provider policy readback conflicts with the exact proposal."); }
                    var terminal = ProviderProfileAudit(request, SecurityAuditOutcome.Succeeded);
                    using (var receipt = HostActivity.BeginAudit(request, terminal))
                    using (var transaction = connection.BeginTransaction())
                    {
                        var sequence = AppendAudit(connection, transaction, intent, previous.Session, terminal,
                            changes: [ProviderProfileChange(request.SessionId, previous.Session.Generation, revision, payload), TaskChange(completed)]);
                        Execute(connection, transaction, "UPDATE session_provider_profile SET audit_sequence=$audit WHERE session_id=$id;",
                            ("$audit", sequence), ("$id", Id(request.SessionId)));
                        WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
                        Commit(transaction, request, token, admitted);
                        receipt.Complete(HostOperationOutcome.Completed);
                    }
                    database.VerifyFiles();
                    var confirmed = ReadProviderProfileRow(connection, request.SessionId);
                    if (confirmed.Held || confirmed.Revision != revision
                        || !Same(confirmed.Desired.Serialize(), payload))
                    { throw new IOException("Provider policy committed but completion is held; inspect exact authority and control receipts."); }
                    heldProviderProfiles.TryRemove(request.SessionId, out _);
                    activity.Complete(HostOperationOutcome.Completed);
                    return confirmed;
                }, token);
        }, token));
    }

    private static SecurityAuditEvent ProviderProfileAudit(HostRequest request, SecurityAuditOutcome outcome) =>
        new(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, ProviderProfileAction, outcome,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
            Id(request.TaskId));

    private static AuthorityChange ProviderProfileChange(HostId<SessionIdentity> session, HostRevision generation,
        long revision, string payload) => Change("session-provider-profile", Id(session), revision,
            generation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + payload);

    private static SessionProviderProfile ReadProviderProfileRow(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        var owner = RequireSession(connection, session);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT generation,revision,CASE WHEN typeof(payload)='text' AND length(CAST(payload AS BLOB))<=4096
                THEN payload ELSE NULL END,audit_sequence
            FROM session_provider_profile WHERE session_id=$id;
            """;
        command.Parameters.AddWithValue("$id", Id(session));
        using var reader = command.ExecuteReader();
        SessionProviderOverrides desired = SessionProviderOverrides.Inherit;
        var revision = 0L;
        if (reader.Read())
        {
            var generation = new HostRevision(reader.GetInt64(0));
            revision = reader.GetInt64(1);
            if (generation.Value > owner.Authority.Generation.Value || reader.IsDBNull(2) || revision <= 0 || owner.State == 2)
            { throw new InvalidDataException("Invalid provider policy lifecycle, generation, revision or payload."); }
            var payload = reader.GetString(2);
            desired = SessionProviderOverrides.Parse(payload);
            ValidateRowAuthority(connection, reader.GetInt64(3), ProviderProfileChange(session, generation, revision, payload));
        }
        using var pending = connection.CreateCommand();
        pending.CommandText = """
            SELECT 1 FROM security_audit_events a
            WHERE json_extract(a.envelope,'$.Request.SessionId.Value')=$id
                AND json_extract(a.envelope,'$.Audit.ActionId')='session.provider-profile'
                AND json_extract(a.envelope,'$.Audit.Outcome')=0
                AND NOT EXISTS(SELECT 1 FROM security_audit_events t
                    WHERE json_extract(t.envelope,'$.Request.TaskId.Value')=json_extract(a.envelope,'$.Request.TaskId.Value')
                        AND json_extract(t.envelope,'$.Request.SessionId.Value')=$id
                        AND json_extract(t.envelope,'$.Audit.ActionId')='session.provider-profile'
                        AND t.sequence>a.sequence AND json_extract(t.envelope,'$.Audit.Outcome')=1) LIMIT 1;
            """;
        pending.Parameters.AddWithValue("$id", Id(session));
        return new(owner.Authority, revision, desired, pending.ExecuteScalar() is not null);
    }

    private static void ValidateProviderProfiles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id FROM session_provider_profile;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) { _ = ReadProviderProfileRow(connection, new(ParseId(reader.GetString(0)))); }
        }
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id,MAX(a.sequence) AS sequence
                FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='session-provider-profile' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN session_provider_profile p ON p.session_id=latest.id
                JOIN work_sessions s ON s.session_id=latest.id
            WHERE s.state<>2 AND (p.audit_sequence IS NULL OR p.audit_sequence<>latest.sequence) LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null) { throw new InvalidDataException("Missing or stale exact-session provider authority."); }
        command.CommandText = """
            SELECT 1 FROM security_audit_events t
            WHERE json_extract(t.envelope,'$.Audit.ActionId')='session.provider-profile'
                AND json_extract(t.envelope,'$.Audit.Outcome')=1
                AND NOT EXISTS(SELECT 1 FROM security_audit_events r
                    WHERE r.sequence<t.sequence AND json_extract(r.envelope,'$.Audit.ActionId')='session.provider-profile'
                        AND json_extract(r.envelope,'$.Audit.Outcome')=0
                        AND json_extract(r.envelope,'$.Request')=json_extract(t.envelope,'$.Request')
                        AND json_extract(r.envelope,'$.SessionGeneration')=json_extract(t.envelope,'$.SessionGeneration')) LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null) { throw new InvalidDataException("Provider policy lost its exact requested audit."); }
    }
}