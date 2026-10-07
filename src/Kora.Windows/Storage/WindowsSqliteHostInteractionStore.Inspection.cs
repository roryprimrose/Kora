using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    ValueTask<EvidenceReadBatch> ICommittedAuthorityAuditReader.ReadAsync(EvidenceQuery query,
        EvidenceReadCheckpoint? checkpoint, HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireLive(request);
        if (request.Origin != RequestOrigin.LocalUi || query.Source != EvidenceSource.AuthorityAudit
            || query.Limit is < 1 or > EvidencePage.MaximumRecords
            || query.Record is { Source: not EvidenceSource.AuthorityAudit })
        {
            throw new InvalidOperationException("Committed audit inspection requires its explicit admitted local-UI source.");
        }
        return new(Task.Run(() =>
        {
            try { return ReadCommittedAudit(query, checkpoint, request, now, cancellationToken); }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested
                && exception is SqliteException or InvalidDataException)
            {
                throw new OperationCanceledException("Committed audit inspection was cancelled.", exception, cancellationToken);
            }
        }, cancellationToken));
    }

    private EvidenceReadBatch ReadCommittedAudit(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
            cancellationToken.ThrowIfCancellationRequested();
            // Do not call Open or a task read here: they validate/migrate and may reacquire this lease.
            using var lease = database.AcquireReadLease(cancellationToken);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(cancellationToken);
            using var transaction = connection.BeginTransaction(deferred: true);
            if (query.SessionId is { } session && RequireSession(connection, session).State == 2)
            {
                throw new InvalidOperationException("Removed sessions are not browsable.");
            }
            var snapshot = ReadAuditSnapshot(connection, identity, checkpoint?.Snapshot);
            var after = checkpoint?.After;
            if (after is not null)
            {
                if (after.Source != EvidenceSource.AuthorityAudit || after.Ordinal != -1
                    || after.CommittedTicks is <= 0 || after.CommittedTicks > snapshot.AuthorityCeiling)
                {
                    throw new InvalidDataException("The committed audit continuation has an invalid position.");
                }
                using var exact = connection.CreateCommand();
                exact.CommandText = "SELECT correlation_id,hash FROM security_audit_events WHERE sequence=$sequence;";
                exact.Parameters.AddWithValue("$sequence", after.CommittedTicks);
                using var exactRow = exact.ExecuteReader();
                if (!exactRow.Read() || !Same(exactRow.GetString(0), after.Id)
                    || !Same(exactRow.GetString(1), after.CommitDigest ?? string.Empty))
                {
                    throw new InvalidDataException("The exact committed audit continuation is missing or changed.");
                }
            }
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.sequence,a.correlation_id,a.previous_hash,a.hash,a.envelope,p.hash
                FROM security_audit_events a LEFT JOIN security_audit_events p ON p.sequence=a.sequence-1
                WHERE a.sequence>$after AND a.sequence<=$ceiling ORDER BY a.sequence LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$after", after?.CommittedTicks ?? 0);
            command.Parameters.AddWithValue("$ceiling", snapshot.AuthorityCeiling!.Value);
            command.Parameters.AddWithValue("$limit", WindowsSqliteEvidenceReader.MaximumScannedRows + 1);
            using var rows = command.ExecuteReader();
            var candidates = new List<EvidenceCandidate>();
            var scannedThrough = after;
            var scanned = 0;
            var hasMore = false;
            var limited = false;
            while (rows.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned > WindowsSqliteEvidenceReader.MaximumScannedRows) { limited = hasMore = true; break; }
                var sequence = rows.GetInt64(0);
                if (sequence != (scannedThrough?.CommittedTicks ?? 0) + 1
                    || !Same(rows.GetString(2), sequence == 1 ? EmptyHash
                        : rows.IsDBNull(5) ? string.Empty : rows.GetString(5)))
                {
                    throw new InvalidDataException("The committed audit prefix has a missing or inconsistent predecessor.");
                }
                var audit = DecodeAuthorityAudit(sequence, rows.GetString(1), rows.GetString(2),
                    rows.GetString(3), rows.GetString(4));
                var position = new EvidencePosition(sequence, EvidenceSource.AuthorityAudit, rows.GetString(1), -1, rows.GetString(3));
                scannedThrough = position;
                var provenance = new AuthorityAuditProvenance(HostInteractionSchema.Version, "security_audit_events", sequence, rows.GetString(3),
                    audit.TraceId, audit.SpanId, audit.IntentRevision, audit.SessionGeneration,
                    audit.Outcome, audit.QuestionId, audit.QuestionRevision, audit.ApprovalId, audit.GrantRevision,
                    audit.Changes.Select(change => new AuthorityAuditChange(change.Kind, change.Id, change.Revision, change.Digest)).ToArray());
                var record = new EvidenceRecord(new(EvidenceSource.AuthorityAudit, new(audit.Audit.CorrelationId)),
                    audit.CommittedAt, audit.DueAt,
                    audit.DueAt <= now ? EvidenceSegmentStatus.ExpiredButPresent : EvidenceSegmentStatus.Present,
                    audit.Request, null, audit.Audit.CorrelationId, audit.Audit.ApprovalId, null,
                    audit.Audit.Category.ToString(), null, audit.Audit.ActionId,
                    new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), audit.Audit, null, [],
                    AuditSequence: sequence, AuthorityProvenance: provenance);
                if (!MatchesAuthority(record, query) || audit.Request.RequestId == request.RequestId) { continue; }
                candidates.Add(new(position, record));
                if (candidates.Count > query.Limit) { hasMore = true; break; }
            }
            if (!hasMore && (scannedThrough?.CommittedTicks ?? 0) != snapshot.AuthorityCeiling)
            {
                throw new InvalidDataException("The committed audit snapshot contains a missing suffix.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same(identity, RequireInspectionIdentity()))
            {
                throw new InvalidDataException("The committed audit store changed during inspection.");
            }
            return new(snapshot, candidates, scannedThrough, hasMore, limited);
    }

    private string RequireInspectionIdentity()
    {
        var identity = database.ReadIdentity();
        if (inspectionIdentity is null || !Same(identity, inspectionIdentity))
        {
            throw new InvalidDataException("The committed authority is uninitialized or replaced; no replacement is admitted.");
        }
        return runId.ToString("D") + ":" + identity;
    }

    private static EvidenceSnapshot ReadAuditSnapshot(SqliteConnection connection, string identity, EvidenceSnapshot? prior)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,hash FROM authority_head WHERE singleton=1;";
        long head;
        string hash;
        using (var rows = command.ExecuteReader())
        {
            if (!rows.Read()) { throw new InvalidDataException("The committed authority head is missing."); }
            head = rows.GetInt64(0);
            hash = rows.GetString(1);
            if (head < 0 || rows.Read()) { throw new InvalidDataException("The committed authority head is invalid."); }
        }
        command.CommandText = "SELECT count(*) FROM security_audit_events WHERE sequence>$head;";
        command.Parameters.AddWithValue("$head", head);
        if ((long)command.ExecuteScalar()! != 0) { throw new InvalidDataException("The authority head omits committed rows."); }
        var ceiling = prior?.AuthorityCeiling ?? head;
        if (ceiling < 0 || ceiling > head || prior is not null && (prior.AuthorityCeiling is null
            || !Same(prior.AuthorityStoreIdentity ?? string.Empty, identity)))
        {
            throw new InvalidDataException("The committed audit snapshot belongs to another store or lifetime.");
        }
        command.CommandText = "SELECT hash FROM security_audit_events WHERE sequence=$ceiling;";
        command.Parameters.AddWithValue("$ceiling", ceiling);
        var digest = ceiling == 0 ? EmptyHash : command.ExecuteScalar() as string
            ?? throw new InvalidDataException("The exact committed audit ceiling is missing.");
        command.CommandText = "SELECT hash FROM security_audit_events WHERE sequence=$head;";
        var headDigest = head == 0 ? EmptyHash : command.ExecuteScalar() as string;
        if (!Same(headDigest ?? string.Empty, hash)
            || prior is not null && !Same(prior.AuthorityCeilingDigest ?? string.Empty, digest))
        {
            throw new InvalidDataException("The committed audit ceiling or head changed.");
        }
        return new(0, 0, 0, 0, AuthorityCeiling: ceiling, AuthorityCeilingDigest: digest,
            AuthorityStoreIdentity: identity);
    }

    private static bool MatchesAuthority(EvidenceRecord record, EvidenceQuery query)
    {
        var request = record.Host!;
        var provenance = record.AuthorityProvenance!;
        return (query.Record is null || query.Record == record.Reference)
            && (query.SessionId is null || request.SessionId == query.SessionId)
            && (query.TaskId is null || request.TaskId == query.TaskId)
            && (query.RequestId is null || request.RequestId == query.RequestId)
            && (query.InvocationId is null || request.InvocationId == query.InvocationId)
            && (query.ApprovalId is null || record.ApprovalId == query.ApprovalId)
            && (query.CorrelationId is null || record.CorrelationId == query.CorrelationId)
            && (query.TraceId is null || Same(provenance.TraceId, query.TraceId))
            && (query.SpanId is null || Same(provenance.SpanId, query.SpanId))
            && (query.FromUtc is null || record.CommittedUtc >= query.FromUtc)
            && (query.UntilUtc is null || record.CommittedUtc <= query.UntilUtc)
            && WindowsSqliteEvidenceReader.Matches(record, query);
    }
}
