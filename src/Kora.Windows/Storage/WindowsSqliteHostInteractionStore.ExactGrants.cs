using System.Collections.Immutable;
using System.Text;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IExactGrantStore
{
    private readonly Lock exactGrantControlGate = new();
    private readonly HashSet<HostId<SessionIdentity>> exactGrantControlSessions = [];
    // No Open/ValidateAuthority here: those collect all grants. Passive reads use the initialized
    // shared private lease and validate only bounded exact rows and their committed provenance.
    public ValueTask<ExactGrantPage> ReadExactGrantPageAsync(ExactGrantCursor? cursor, CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(cancellationToken);
            using var transaction = connection.BeginTransaction(deferred: true);
            var snapshot = ReadAuditSnapshot(connection, identity, null);
            var head = (Sequence: snapshot.AuthorityCeiling!.Value, Hash: snapshot.AuthorityCeilingDigest!);
            if (cursor is not null && (!Same(cursor.StoreIdentity, identity)
                || cursor.AuditSequence != head.Sequence || !Same(cursor.AuditHash, head.Hash) || cursor.After == Guid.Empty))
            {
                throw new InvalidDataException("The exact-grant continuation changed; start a fresh inventory.");
            }
            if (cursor is not null && ReadExactGrant(connection, new(cursor.After), identity) is null)
            {
                throw new InvalidDataException("The exact-grant continuation record is unavailable.");
            }
            using var command = ExactGrantCommand(connection, cursor?.After, exact: null);
            using var rows = command.ExecuteReader();
            var records = ImmutableArray.CreateBuilder<ExactGrantInspection>();
            var bytes = 0;
            var more = false;
            while (rows.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = DecodeExactGrant(connection, rows, identity);
                var size = Encoding.UTF8.GetByteCount(HostInteractionCodec.Encode(item));
                if (records.Count == ExactGrantPage.MaximumRecords || bytes + size > ExactGrantPage.MaximumBytes)
                {
                    more = true;
                    break;
                }
                records.Add(item);
                bytes += size;
            }
            cancellationToken.ThrowIfCancellationRequested();
            database.VerifyFiles();
            if (!Same(identity, RequireInspectionIdentity()))
            {
                throw new InvalidDataException("The initialized exact-grant store changed during inspection.");
            }
            return new ExactGrantPage(records.ToImmutable(), more
                ? new(identity, head.Sequence, head.Hash, records[^1].Grant.Id.Value) : null);
        }, cancellationToken));

    public ValueTask<ExactGrantInspection?> InspectExactGrantAsync(HostId<ApprovalIdentity> id, CancellationToken cancellationToken)
    {
        id.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(cancellationToken);
            using var transaction = connection.BeginTransaction(deferred: true);
            _ = ReadAuditSnapshot(connection, identity, null);
            var record = ReadExactGrant(connection, id, identity);
            cancellationToken.ThrowIfCancellationRequested();
            database.VerifyFiles();
            return record;
        }, cancellationToken));
    }

    public async ValueTask<WorkSessionAuthorization> CreateExactGrantControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken)
    {
        var session = await RunHostMutationAsync(request, "session.create.exact-grant-control", (connection, transaction, intent, audit) =>
        {
            if (request.Origin != RequestOrigin.LocalUi || ReadSession(connection, request.SessionId) is not null || !admitted())
            {
                throw new InvalidOperationException("Exact-grant control requires fresh original native user input.");
            }
            var session = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var sequence = AppendAudit(connection, transaction, intent, session, audit, changes: [SessionChange(session, 0)]);
            WriteSession(connection, transaction, session, 0, sequence);
            return session;
        }, cancellationToken, admitted, requireIdle: false).ConfigureAwait(false);
        lock (exactGrantControlGate) { exactGrantControlSessions.Add(session.SessionId); }
        return session;
    }

    public ValueTask<HostInteractionDecision> RevokeExactGrantAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantInspection preview, SecurityAuditEvent requested, Func<bool> admitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        RequireLive(request);
        if (request.Origin != RequestOrigin.LocalUi || !Same(requested.ActionId, "approval.revoke-exact")
            || requested.Outcome != SecurityAuditOutcome.Requested || requested.ApprovalId != preview.Grant.Id.Value)
        {
            throw new InvalidOperationException("Exact revocation requires its original native-user control intent and typed audit.");
        }
        return new(Task.Run(() => tasks.WithCommittedIntent(request, (connection, intent) =>
        {
            using var storage = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
            ValidateAuthority(connection);
            using var transaction = connection.BeginTransaction();
            var control = RequireSession(connection, request.SessionId).Authority;
            bool isCurrentControl;
            lock (exactGrantControlGate) { isCurrentControl = exactGrantControlSessions.Contains(request.SessionId); }
            if (!isCurrentControl || !control.IsActive
                || control.Generation != controlGeneration || !admitted())
            {
                throw new InvalidOperationException("Exact-grant control session, ownership or privacy is unavailable.");
            }
            var current = ReadExactGrant(connection, preview.Grant.Id, RequireInspectionIdentity());
            HostInteractionDecision decision;
            if (current != preview || current.Grant.Status != OperationGrantStatus.Active)
            {
                decision = new(HostInteractionOutcome.Conflict, "exact-grant-preview-changed");
            }
            else
            {
                decision = new(HostInteractionOutcome.Revoked, "explicit-user-revocation",
                    Grant: current.Grant.Revoke("explicit-user-revocation"));
            }
            var terminal = requested.WithOutcome(decision.Grant is null ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Succeeded,
                decision.Reason);
            var sequence = AppendAudit(connection, transaction, intent, control, terminal, decision,
                decision.Grant is { } changed ? [GrantChange(changed)] : [], requested, preview.Grant.Revision);
            if (decision.Grant is { } revoked) { WriteGrant(connection, transaction, revoked, sequence); }
            Commit(transaction, request, cancellationToken, admitted);
            storage.Complete(HostOperationOutcome.Completed);
            return decision;
        }, cancellationToken), cancellationToken));
    }

    private static SqliteCommand ExactGrantCommand(SqliteConnection connection, Guid? after, HostId<ApprovalIdentity>? exact)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT approval_id,revision,CASE WHEN length(CAST(payload AS BLOB))<=$bytes THEN payload ELSE NULL END,
                perpetual,session_id,audit_sequence,
                EXISTS(SELECT 1 FROM scoped_grants s WHERE s.approval_id=g.approval_id)
                    AND EXISTS(SELECT 1 FROM perpetual_grants p WHERE p.approval_id=g.approval_id)
            FROM (
                SELECT approval_id,revision,payload,0 AS perpetual,session_id,audit_sequence FROM scoped_grants
                UNION ALL SELECT approval_id,revision,payload,1,NULL,audit_sequence FROM perpetual_grants) g
            WHERE approval_id>$after AND ($exact IS NULL OR approval_id=$exact)
            ORDER BY approval_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$bytes", ExactGrantPage.MaximumStoredRecordBytes);
        command.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
        command.Parameters.AddWithValue("$exact", exact is { } id ? Id(id) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", exact is null ? ExactGrantPage.MaximumRecords + 1 : 2);
        return command;
    }

    private static ExactGrantInspection? ReadExactGrant(SqliteConnection connection, HostId<ApprovalIdentity> id, string identity)
    {
        using var command = ExactGrantCommand(connection, null, id);
        using var rows = command.ExecuteReader();
        if (!rows.Read()) { return null; }
        var record = DecodeExactGrant(connection, rows, identity);
        if (rows.Read()) { throw new InvalidDataException("An exact grant ID occurs in multiple partitions."); }
        return record;
    }

    private static ExactGrantInspection DecodeExactGrant(SqliteConnection connection, SqliteDataReader rows, string identity)
    {
        if (rows.IsDBNull(2) || rows.GetInt64(6) != 0) { throw new InvalidDataException("An exact grant exceeds the metadata limit or has duplicate identity."); }
        var json = rows.GetString(2);
        var grant = HostInteractionCodec.Decode<OperationGrant>(json);
        grant.Validate();
        if (!Same(Id(grant.Id), rows.GetString(0)) || grant.Revision.Value != rows.GetInt64(1)
            || (grant.Scope == OperationGrantScope.Perpetual) != (rows.GetInt64(3) == 1)
            || grant.Scope != OperationGrantScope.Perpetual
                && (rows.IsDBNull(4) || !Same(Id(grant.ApprovedProposal.Request.SessionId), rows.GetString(4))))
        {
            throw new InvalidDataException("The exact grant's authoritative identity, scope or revision is invalid.");
        }
        ValidateExactGrantAudit(connection, rows.GetInt64(5), Change("grant", Id(grant.Id), grant.Revision.Value, json));
        var origin = ReadExactGrantOrigin(connection, grant.ApprovedProposal.Request.SessionId);
        if (origin is null && grant.Scope != OperationGrantScope.Perpetual
            || origin is not null && (origin.Authority.Generation.Value <= 0 || origin.State is < 0 or > 2))
        {
            throw new InvalidDataException("The exact grant's originating session metadata is invalid.");
        }
        return new(identity, grant, origin?.Authority, origin?.State == 2);
    }

    private static SessionRow? ReadExactGrantOrigin(SqliteConnection connection, HostId<SessionIdentity> id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id,generation,state,audit_sequence FROM work_sessions WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(id));
        using var row = command.ExecuteReader();
        if (!row.Read()) { return null; }
        var session = DecodeSession(row);
        ValidateExactGrantAudit(connection, row.GetInt64(3), SessionChange(session.Authority, checked((int)session.State)));
        return session;
    }

    private static void ValidateExactGrantAudit(SqliteConnection connection, long sequence, AuthorityChange change)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT correlation_id,previous_hash,hash,
                CASE WHEN length(CAST(envelope AS BLOB))<=131072 THEN envelope ELSE NULL END
            FROM security_audit_events WHERE sequence=$sequence;
            """;
        command.Parameters.AddWithValue("$sequence", sequence);
        using var row = command.ExecuteReader();
        if (!row.Read() || row.IsDBNull(3))
        {
            throw new InvalidDataException("Exact-grant committed audit metadata is missing or oversized.");
        }
        var audit = DecodeAuthorityAudit(sequence, row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3));
        if (!audit.Changes.Contains(change))
        {
            throw new InvalidDataException("Exact-grant metadata is not bound to its authoritative typed audit.");
        }
    }
}
