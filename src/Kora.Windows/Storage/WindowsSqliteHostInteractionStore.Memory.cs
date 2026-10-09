using System.Collections.Immutable;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private void MigrateMemory(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        ValidateQueue(connection);
        ValidateRetention(connection);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='memory' LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("A downgraded memory schema cannot replace committed memory or tombstones.");
        }
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, HostInteractionSchema.MemoryProfileTable + ";" + HostInteractionSchema.MemoryTable);
        SeedMemoryProfile(connection, transaction);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    private static void SeedMemoryProfile(SqliteConnection connection, SqliteTransaction transaction) =>
        Execute(connection, transaction, "INSERT INTO memory_profile VALUES(1,$id);", ("$id", Guid.NewGuid().ToString("D")));

    private static HostId<DeviceProfileIdentity> ReadMemoryProfile(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT singleton,profile_id FROM memory_profile;";
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != 1)
        {
            throw new InvalidDataException("The private memory partition identity is missing.");
        }
        var profile = new HostId<DeviceProfileIdentity>(ParseId(reader.GetString(1)));
        if (reader.Read()) { throw new InvalidDataException("The private memory partition identity is ambiguous."); }
        return profile;
    }

    public ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token)
    {
        RequireLive(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(controlRevision);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var session = RequireSession(connection, request.SessionId);
            if (!session.Authority.IsActive)
            {
                throw new InvalidOperationException("Durable memory requires an exact active host-owned session.");
            }
            return new MemoryBoundary(ReadMemoryProfile(connection), request.SessionId, session.Authority.Generation,
                null, null, null, controlRevision, true, true, true);
        }, token));
    }

    public ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
        Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(admitted);
        return RunHostMutationAsync(request, "memory.mutate", (connection, transaction, intent, audit) =>
        {
            var session = RequireSession(connection, request.SessionId).Authority;
            if (!admitted() || !session.IsActive || boundary.Session != request.SessionId
                || boundary.Generation != session.Generation || boundary.Profile != ReadMemoryProfile(connection)
                || boundary.Project is not null || boundary.Source is not null || boundary.SourceRevision is not null
                || MemoryPolicy.CheckBoundary(MemoryScope.Session(request.SessionId), boundary) != MemoryReason.None)
            {
                throw new InvalidOperationException("The current private session memory boundary is closed.");
            }
            var all = ReadMemory(connection);
            var rows = all.Where(row => row.Scope.Identity == request.SessionId.Value).ToImmutableArray();
            var commit = transition(rows);
            if (commit.Replacement is not { } replacement) { return commit.Result; }
            RequireFreshMetadataIntent(intent);
            ValidateMemoryRecord(replacement, boundary.Profile);
            if (replacement.Scope.Identity != request.SessionId.Value || commit.Result.Record?.Id != replacement.Id
                || commit.Result.Outcome != MemoryOutcome.Succeeded
                || MemoryPolicy.CheckLineage(replacement, boundary) != MemoryReason.None)
            {
                throw new InvalidDataException("A durable memory mutation must retain exact admitted session lineage.");
            }
            var previous = all.FirstOrDefault(row => row.Id == replacement.Id);
            if (replacement.Retention == MemoryRetentionState.Enabled
                && (previous is null || previous.Review != MemoryReviewState.Admitted))
            {
                var reviewed = commit.ReviewedOriginal;
                if (reviewed is null || !MemoryPolicy.CanAdmit(reviewed, boundary)
                    || MemoryPolicy.CheckLineage(reviewed, boundary) != MemoryReason.None
                    || MemoryPolicy.ValidateCandidate(reviewed.Candidate) != MemoryReason.None
                    || replacement != reviewed with
                    {
                        Revision = new(checked(reviewed.Revision.Value + 1)),
                        Review = MemoryReviewState.Admitted, Retention = MemoryRetentionState.Enabled,
                    })
                {
                    throw new InvalidDataException("Durable admission requires the exact original reviewed revision and candidate.");
                }
            }
            if (previous != commit.Original || previous?.Retention == MemoryRetentionState.Forgotten
                || previous is not null && (replacement.Revision.Value != checked(previous.Revision.Value + 1)
                    || replacement.Scope != previous.Scope || replacement.CreatedAt != previous.CreatedAt)
                || previous is null && (replacement.Review != MemoryReviewState.Admitted
                    || replacement.Retention != MemoryRetentionState.Enabled))
            {
                throw new InvalidOperationException("The original durable memory revision, identity or tombstone conflicts.");
            }
            if (previous is not null && (replacement.Retention == MemoryRetentionState.Forgotten
                    && replacement.Lineage != previous.Lineage
                || replacement.Retention == MemoryRetentionState.Enabled && previous.Retention == MemoryRetentionState.Disabled
                || replacement.Retention == MemoryRetentionState.Disabled
                    && (previous.Retention != MemoryRetentionState.Enabled || replacement.Lineage != previous.Lineage
                        || replacement.Candidate != previous.Candidate || replacement.Receipt != previous.Receipt)
                || replacement.Retention == MemoryRetentionState.Pending && replacement.Lineage != previous.Lineage
                    && (replacement.Lineage.Request != request || replacement.Lineage.Origin != MemoryProposalOrigin.User)))
            {
                throw new InvalidDataException("A durable memory mutation changed immutable original lineage or skipped a disabled-state fence.");
            }
            if (previous is null && all.Length >= MemoryPolicy.MaximumEntries)
            {
                return new MemoryResult(MemoryOutcome.CapacityExceeded, MemoryReason.Capacity);
            }
            if (previous?.Candidate is not null && replacement.Candidate is null)
            {
                database.RequireEmptyArtifactInventory();
            }
            Execute(connection, transaction, "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;");
            var completed = intent.Next(HostTaskState.Succeeded);
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [MemoryChange(replacement), TaskChange(completed)]);
            WriteMemory(connection, transaction, replacement, sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            TouchActivity(connection, transaction, request.SessionId);
            return commit.Result;
        }, token, admitted, requireIdle: false);
    }

    private static AuthorityChange MemoryChange(MemoryRecord record) =>
        Change("memory", Id(record.Id), record.Revision.Value, HostInteractionCodec.Encode(MemoryData.From(record)));

    private static void WriteMemory(SqliteConnection connection, SqliteTransaction transaction, MemoryRecord record, long sequence) =>
        Execute(connection, transaction, """
            INSERT INTO reviewed_memory VALUES($id,$session,$revision,$payload,$audit)
            ON CONFLICT(memory_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
            """, ("$id", Id(record.Id)), ("$session", record.Scope.Identity.ToString("D")), ("$revision", record.Revision.Value),
            ("$payload", HostInteractionCodec.Encode(MemoryData.From(record))), ("$audit", sequence));

    private static ImmutableArray<MemoryRecord> ReadMemory(SqliteConnection connection)
    {
        var profile = ReadMemoryProfile(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT memory_id,session_id,revision,payload,audit_sequence FROM reviewed_memory ORDER BY memory_id;";
        using var reader = command.ExecuteReader();
        var rows = ImmutableArray.CreateBuilder<MemoryRecord>();
        while (reader.Read())
        {
            if (rows.Count >= MemoryPolicy.MaximumEntries) { throw new InvalidDataException("The memory identity inventory is oversized."); }
            var record = HostInteractionCodec.Decode<MemoryData>(reader.GetString(3)).ToRecord();
            ValidateMemoryRecord(record, profile);
            if (!Same(Id(record.Id), reader.GetString(0)) || !Same(record.Scope.Identity.ToString("D"), reader.GetString(1))
                || record.Revision.Value != reader.GetInt64(2))
            {
                throw new InvalidDataException("The saved memory identity, scope or revision projection conflicts.");
            }
            ValidateRowAuthority(connection, reader.GetInt64(4), MemoryChange(record));
            var owner = RequireSession(connection, new(record.Scope.Identity));
            if (record.Retention != MemoryRetentionState.Forgotten
                && (!owner.Authority.IsActive || record.Lineage.SessionGeneration != owner.Authority.Generation))
            {
                throw new InvalidDataException("Unrevoked memory conflicts with its owning session lifecycle.");
            }
            rows.Add(record);
        }
        return rows.ToImmutable();
    }

    private static void ValidateMemoryRecord(MemoryRecord record, HostId<DeviceProfileIdentity> profile)
    {
        if (record.Id.Value == Guid.Empty || record.Revision.Value <= 0 || record.Lineage is null
            || record.Lineage.Request is null || record.Lineage.Profile != profile
            || record.Scope.Kind != MemoryScopeKind.Session || record.Scope.Identity != record.Lineage.Request.SessionId.Value
            || record.CreatedAt.Offset != TimeSpan.Zero || record.Lineage.SessionGeneration.Value <= 0
            || record.Lineage.Origin is not (MemoryProposalOrigin.User or MemoryProposalOrigin.Model)
            || record.Lineage.Source is not null || record.Lineage.SourceRevision is not null
            || !Enum.IsDefined(record.Review) || !Enum.IsDefined(record.Retention))
        {
            throw new InvalidDataException("The saved memory identity, original lineage or scope is invalid.");
        }
        if (record.Retention == MemoryRetentionState.Forgotten)
        {
            if (record.Candidate is not null || record.Receipt is not null)
            {
                throw new InvalidDataException("A memory tombstone retains content or review authority.");
            }
            return;
        }
        if (record.Retention == MemoryRetentionState.Pending)
        {
            if (record.Review != MemoryReviewState.Proposed || record.Candidate is not null || record.Receipt is not null)
            {
                throw new InvalidDataException("Unreviewed memory content cannot be persisted.");
            }
            return;
        }
        var receipt = record.Receipt;
        if (record.Review != MemoryReviewState.Admitted
            || record.Retention is not (MemoryRetentionState.Enabled or MemoryRetentionState.Disabled)
            || MemoryPolicy.ValidateCandidate(record.Candidate) != MemoryReason.None || receipt is null
            || receipt.Request.Value == Guid.Empty || receipt.Revision.Value <= 0
            || receipt.Revision.Value >= record.Revision.Value || receipt.ReviewedAt.Offset != TimeSpan.Zero
            || receipt.ReviewedAt < record.CreatedAt || receipt.Boundary is null
            || MemoryPolicy.CheckBoundary(record.Scope, receipt.Boundary) != MemoryReason.None
            || MemoryPolicy.CheckLineage(record, receipt.Boundary) != MemoryReason.None
            || receipt.Boundary.Profile != profile || receipt.Boundary.Project is not null
            || receipt.Boundary.Source is not null)
        {
            throw new InvalidDataException("The saved memory value or exact reviewed lineage is invalid.");
        }
    }

    private static void ValidateMemory(SqliteConnection connection)
    {
        _ = ReadMemory(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id, MAX(a.sequence) AS sequence
                FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='memory' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN reviewed_memory m ON m.memory_id=latest.id
            WHERE m.audit_sequence IS NULL OR m.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Committed memory or its non-reusable tombstone is missing or stale.");
        }
    }

    private static MemoryRecord[] MemoryInvalidations(SqliteConnection connection, HostId<SessionIdentity> session) =>
        ReadMemory(connection).Where(row => row.Scope.Identity == session.Value && row.Retention != MemoryRetentionState.Forgotten)
            .Select(row => row with
            {
                Revision = new(checked(row.Revision.Value + 1)), Candidate = null, Receipt = null,
                Retention = MemoryRetentionState.Forgotten,
            }).ToArray();

    private sealed record MemoryData(HostId<MemoryIdentity> Id, HostRevision Revision, HostId<SessionIdentity> Session,
        MemoryLineage Lineage, MemoryCandidate? Candidate, DateTimeOffset CreatedAt, MemoryReviewState Review,
        MemoryRetentionState Retention, MemoryReviewReceipt? Receipt)
    {
        internal static MemoryData From(MemoryRecord record) =>
            new(record.Id, record.Revision, new(record.Scope.Identity), record.Lineage, record.Candidate,
                record.CreatedAt, record.Review, record.Retention, record.Receipt);

        internal MemoryRecord ToRecord()
        {
            try
            {
                return new(Id, Revision, MemoryScope.Session(Session), Lineage, Candidate, CreatedAt, Review, Retention, Receipt);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("The saved memory scope identity is invalid.", exception);
            }
        }
    }
}
