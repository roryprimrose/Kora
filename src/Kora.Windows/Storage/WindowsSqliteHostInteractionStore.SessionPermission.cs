using System.Collections.Immutable;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionPermissionStore
{
    private void MigrateSessionPermission(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateConsolidatedAuthority(connection);
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, HostInteractionSchema.SessionPermissionTable);
        Execute(connection, transaction, HostInteractionSchema.SessionAdmissionTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            return ReadSessionPermission(connection, RequireSession(connection, session).Authority);
        }, token));
    }

    public ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(
        HostRequest request, SessionPermissionPolicy expected, SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(admitted);
        if (!Enum.IsDefined(desired)) { throw new ArgumentOutOfRangeException(nameof(desired)); }
        return RunHostMutationAsync(request, "session.permission.set", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            var session = RequireSession(connection, request.SessionId).Authority;
            var previous = ReadSessionPermission(connection, session);
            if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
                || !session.IsActive || previous != expected || !admitted())
            { throw new InvalidOperationException("Permission changes require a fresh native confirmation of this exact Active session and policy revision."); }
            var next = previous with { Revision = new(checked(previous.Revision.Value + 1)), Level = desired };
            var questions = ReadQuestions(connection, request.SessionId)
                .Where(question => question.Status == QuestionStatus.Pending)
                .Select(question => question with { Key = question.Key.Next(), Status = QuestionStatus.Cancelled }).ToArray();
            var changes = questions.Select(QuestionChange).Append(PermissionChange(next)).ToArray();
            var sequence = AppendAudit(connection, transaction, intent, session, audit, changes: changes,
                requestedAudit: audit.WithOutcome(SecurityAuditOutcome.Requested), permissionPolicy: next);
            Execute(connection, transaction, """
                INSERT INTO session_permission VALUES($id,$generation,$revision,$level,$audit)
                ON CONFLICT(session_id) DO UPDATE SET generation=excluded.generation,revision=excluded.revision,
                    level=excluded.level,audit_sequence=excluded.audit_sequence;
                DELETE FROM session_admissions WHERE session_id=$id;
                DELETE FROM host_observations WHERE session_id=$id;
                """, ("$id", Id(request.SessionId)), ("$generation", next.Generation.Value),
                ("$revision", next.Revision.Value), ("$level", (int)next.Level), ("$audit", sequence));
            foreach (var question in questions) { WriteQuestion(connection, transaction, question, sequence); }
            return next;
        }, token, admitted, requireIdle: false);
    }

    private static AuthorityChange PermissionChange(SessionPermissionPolicy policy) =>
        Change("session-permission", Id(policy.SessionId), policy.Revision.Value, HostInteractionCodec.Encode(policy));

    private static AuthorityChange AdmissionChange(SessionPermissionAdmission admission) =>
        Change("session-admission", admission.Id.ToString("D"), admission.Dispatched ? 2 : 1, HostInteractionCodec.Encode(admission));

    private static SessionPermissionPolicy ReadSessionPermission(SqliteConnection connection, WorkSessionAuthorization session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT generation,revision,level,audit_sequence FROM session_permission WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session.SessionId));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return SessionPermissionPolicy.Default(session); }
        var value = new SessionPermissionPolicy(session.SessionId, new(reader.GetInt64(0)), new(reader.GetInt64(1)),
            (SessionPermissionLevel)reader.GetInt32(2));
        value.Validate(session);
        ValidateRowAuthority(connection, reader.GetInt64(3), PermissionChange(value));
        return value;
    }

    private static void ValidateSessionPermissions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id FROM session_permission;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = new HostId<SessionIdentity>(ParseId(reader.GetString(0)));
                _ = ReadSessionPermission(connection, RequireSession(connection, id).Authority);
            }
        }
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id, MAX(a.sequence) AS sequence
                FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
                JOIN work_sessions s ON s.session_id=json_extract(c.value,'$.Id')
                WHERE json_extract(c.value,'$.Kind')='session-permission'
                    AND json_extract(c.value,'$.Revision') IS NOT NULL
                    AND a.sequence>s.audit_sequence
                GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN session_permission p ON p.session_id=latest.id
            WHERE p.audit_sequence IS NULL OR p.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        { throw new InvalidDataException("Committed session permission authority is missing or stale."); }
    }

    private ImmutableArray<SessionPermissionAdmission> ReadSessionAdmissions(SqliteConnection connection, HostRequest request)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT admission_id,payload,audit_sequence FROM session_admissions WHERE session_id=$id AND run_id=$run;";
        command.Parameters.AddWithValue("$id", Id(request.SessionId));
        command.Parameters.AddWithValue("$run", runId.ToString("D"));
        using var reader = command.ExecuteReader();
        var result = ImmutableArray.CreateBuilder<SessionPermissionAdmission>();
        while (reader.Read())
        {
            var value = HostInteractionCodec.Decode<SessionPermissionAdmission>(reader.GetString(1));
            if (value.Id != ParseId(reader.GetString(0)))
            { throw new InvalidDataException("The invocation admission identity is corrupt."); }
            ValidateRowAuthority(connection, reader.GetInt64(2),
                Change("session-admission", value.Id.ToString("D"), value.Dispatched ? 2 : 1, reader.GetString(1)));
            if (value.Proposal.Request == request) { result.Add(value); }
        }
        return result.ToImmutable();
    }

    private void WriteSessionAdmissions(SqliteConnection connection, SqliteTransaction transaction,
        HostInteractionSnapshot before, HostInteractionSnapshot after, long sequence)
    {
        foreach (var item in after.Admissions.Where(item => !before.Admissions.Contains(item)))
        {
            if (item.Proposal != after.Proposal || item.Policy != after.EffectivePermission)
            { throw new InvalidDataException("An invocation admission must bind the current exact proposal and session policy."); }
            var payload = HostInteractionCodec.Encode(item);
            Execute(connection, transaction, """
                INSERT INTO session_admissions VALUES($id,$session,$run,$payload,$audit)
                ON CONFLICT(admission_id) DO UPDATE SET payload=excluded.payload,audit_sequence=excluded.audit_sequence
                    WHERE session_admissions.run_id=excluded.run_id;
                """, ("$id", item.Id.ToString("D")), ("$session", Id(item.Proposal.Request.SessionId)),
                ("$run", runId.ToString("D")), ("$payload", payload), ("$audit", sequence));
        }
    }
}
