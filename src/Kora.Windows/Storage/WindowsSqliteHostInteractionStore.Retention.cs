using System.Globalization;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public const int MaximumRetentionSessions = 32;

    private void MigrateRetention(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        ValidateQueue(connection);
        Execute(connection, transaction, HostInteractionSchema.RetentionTable);
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT session_id FROM work_sessions ORDER BY session_id;";
        var sessions = new List<HostId<SessionIdentity>>();
        using (var reader = read.ExecuteReader())
        {
            while (reader.Read()) { sessions.Add(new(ParseId(reader.GetString(0)))); }
        }
        // Older schemas cannot establish the activity clock. Give them a conservative migration baseline.
        foreach (var session in sessions)
        {
            token.ThrowIfCancellationRequested();
            SeedActivity(connection, transaction, session);
        }
        checkpoint?.BeforeCommit(connection, transaction);
    }

    private void SeedActivity(SqliteConnection connection, SqliteTransaction transaction, HostId<SessionIdentity> session)
    {
        var settings = sessionRetentionPolicy.Settings;
        Execute(connection, transaction, """
            INSERT INTO session_retention VALUES($id,$now,$archive,$delete,0,0,NULL)
            ON CONFLICT(session_id) DO NOTHING;
            """, ("$id", Id(session)), ("$now", Timestamp(time.GetUtcNow())),
            ("$archive", settings.ArchiveDays), ("$delete", settings.DeleteDays));
    }

    private void TouchActivity(SqliteConnection connection, SqliteTransaction transaction, HostId<SessionIdentity> session)
    {
        var settings = sessionRetentionPolicy.Settings;
        // UTC rollback never moves the clock backwards or shortens an already established inactivity interval.
        Execute(connection, transaction, """
            UPDATE session_retention SET last_activity=MAX(last_activity,$now),
                archive_days=$archive,delete_days=$delete WHERE session_id=$id AND purged=0;
            """, ("$id", Id(session)), ("$now", Timestamp(time.GetUtcNow())),
            ("$archive", settings.ArchiveDays), ("$delete", settings.DeleteDays));
    }

    private void ObserveTaskActivity(SqliteConnection connection, SqliteTransaction transaction, HostTaskRecord record,
        bool substantiveIntent)
    {
        if (substantiveIntent)
        {
            TouchActivity(connection, transaction, record.Request.SessionId);
            return;
        }
        if (record.State is not (HostTaskState.DispatchRecorded or HostTaskState.Succeeded or HostTaskState.Failed or HostTaskState.Cancelled))
        {
            return;
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM security_audit_events WHERE json_extract(envelope,'$.Request.TaskId.Value')=$task
                AND (json_extract(envelope,'$.Audit.ActionId') LIKE 'session.%'
                    OR json_extract(envelope,'$.Audit.ActionId') LIKE 'configuration.%'
                    OR json_extract(envelope,'$.Audit.ActionId') LIKE 'queue.%'
                    OR json_extract(envelope,'$.Audit.ActionId') LIKE 'host.task.recovery%') LIMIT 1;
            """;
        command.Parameters.AddWithValue("$task", Id(record.Request.TaskId));
        if (command.ExecuteScalar() is null) { TouchActivity(connection, transaction, record.Request.SessionId); }
    }

    public ValueTask<SessionRetentionState> ReadRetentionAsync(HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            return ReadRetention(connection, session);
        }, token));
    }

    private static SessionRetentionState ReadRetention(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_activity,archive_days,delete_days,perpetual,purged,exemption_audit FROM session_retention WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { throw new InvalidDataException("The session meaningful-activity clock is missing."); }
        try
        {
            var settings = new SessionRetentionSettings(checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)));
            var text = reader.GetString(0);
            var last = DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);
            if (!Same(text, Timestamp(last))) { throw new InvalidDataException("The activity clock is not canonical UTC."); }
            var perpetual = reader.GetInt64(3);
            var purged = reader.GetInt64(4);
            if (perpetual is not (0 or 1) || purged is not (0 or 1))
            {
                throw new InvalidDataException("The session retention flags are invalid.");
            }
            if (reader.IsDBNull(5))
            {
                if (perpetual != 0) { throw new InvalidDataException("Perpetual marking has no committed host audit."); }
            }
            else
            {
                ValidateRowAuthority(connection, reader.GetInt64(5), ExemptionChange(session, perpetual == 1));
            }
            return new(session, last, last.AddDays(settings.ArchiveDays), last.AddDays(settings.DeleteDays),
                perpetual == 1, purged == 1);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The saved session activity clock or retention policy is invalid.", exception);
        }
    }

    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static AuthorityChange ExemptionChange(HostId<SessionIdentity> session, bool perpetual) =>
        Change("retention", Id(session), 1, perpetual ? "perpetual" : "ordinary");

    public ValueTask SetPerpetualAsync(HostRequest request, HostRevision generation, bool perpetual,
        Func<bool> admitted, CancellationToken token) =>
        new(RunHostMutationAsync(request, "session.retention.perpetual", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            var row = RequireSession(connection, request.SessionId);
            if (row.State == 2 || row.Authority.Generation != generation)
            {
                throw new InvalidOperationException("Perpetual marking requires the exact current non-removed session.");
            }
            var sequence = AppendAudit(connection, transaction, intent, row.Authority, audit,
                changes: [ExemptionChange(request.SessionId, perpetual)]);
            Execute(connection, transaction, "UPDATE session_retention SET perpetual=$perpetual,exemption_audit=$audit WHERE session_id=$id;",
                ("$perpetual", perpetual ? 1 : 0), ("$audit", sequence), ("$id", Id(request.SessionId)));
            return true;
        }, token, admitted, requireIdle: false).AsTask());

    private static void ValidateRetention(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT session_id FROM work_sessions;";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read()) { _ = ReadRetention(connection, new(ParseId(reader.GetString(0)))); }
            }
            command.CommandText = """
                SELECT 1 FROM (
                    SELECT json_extract(c.value,'$.Id') AS id,MAX(a.sequence) AS sequence
                    FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
                    WHERE json_extract(c.value,'$.Kind')='retention' GROUP BY json_extract(c.value,'$.Id')
                ) latest LEFT JOIN session_retention r ON r.session_id=latest.id
                WHERE r.exemption_audit IS NULL OR r.exemption_audit<>latest.sequence
                UNION ALL
                SELECT 1 FROM session_retention r JOIN work_sessions s ON s.session_id=r.session_id
                WHERE r.purged=1 AND (
                    s.state<>2 OR EXISTS(SELECT 1 FROM host_tasks WHERE session_id=r.session_id)
                    OR EXISTS(SELECT 1 FROM host_questions WHERE session_id=r.session_id)
                    OR EXISTS(SELECT 1 FROM host_observations WHERE session_id=r.session_id)
                    OR EXISTS(SELECT 1 FROM scoped_grants WHERE session_id=r.session_id)
                    OR EXISTS(SELECT 1 FROM session_metadata WHERE session_id=r.session_id)
                    OR EXISTS(SELECT 1 FROM session_queue WHERE session_id=r.session_id)
                    OR EXISTS(SELECT 1 FROM session_history WHERE session_id=r.session_id AND source<>'{}'))
                LIMIT 1;
                """;
            if (command.ExecuteScalar() is not null)
            {
                throw new InvalidDataException("Session retention exemption or deletion acceptance disagrees with committed authority/content.");
            }
        }
    public ValueTask<SessionRetentionBatch> ApplyRetentionAsync(Func<bool> admitted,
        Func<HostId<SessionIdentity>, CancellationToken, ValueTask> revokeSources, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(revokeSources);
        _ = sessionRetentionPolicy.Settings;
        var owner = HostActivity.RequireCurrent();
        if (owner.Request.Origin != RequestOrigin.HostSystem || owner.Activity!.IsStopped || !admitted())
        {
            throw new InvalidOperationException("Retention requires a live owning host-system lifecycle admission.");
        }
        return new(Task.Run(() => ExecuteRetentionAsync(admitted, revokeSources, token), token));
    }

    private async Task<SessionRetentionBatch> ExecuteRetentionAsync(Func<bool> admitted,
        Func<HostId<SessionIdentity>, CancellationToken, ValueTask> revokeSources, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Retention);
        try
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            database.RequireEmptyArtifactInventory();
            var candidates = ReadRetentionCandidates(connection, token);
            var archived = 0;
            var deleted = 0;
            var held = 0;
            foreach (var id in candidates.Take(MaximumRetentionSessions))
            {
                token.ThrowIfCancellationRequested();
                if (!admitted()) { throw new InvalidOperationException("Host retention admission was revoked."); }
                if (HasRetentionHold(connection, id)) { held++; continue; }
                var clock = ReadRetention(connection, id);
                var deleting = clock.DeleteDue <= time.GetUtcNow();
                await revokeSources(id, token).ConfigureAwait(false);
                using var host = HostActivity.BeginRoot(
                    new(new(Guid.NewGuid()), id, new(Guid.NewGuid()), RequestOrigin.HostSystem),
                    HostActivityLayer.Windows, HostOperation.Retention);
                if (deleting)
                {
                    await DeleteArtifactsAsync(id, () =>
                    {
                        tasks.PurgeRetiredSession(id, token);
                        PurgeOrArchive(connection, id, deleting: true, admitted, token);
                        return Task.CompletedTask;
                    }, token).ConfigureAwait(false);
                    CompleteDeletion(connection, id, admitted);
                }

                else { PurgeOrArchive(connection, id, deleting: false, admitted, token); }
                if (deleting) { deleted++; } else { archived++; }
                host.Complete(HostOperationOutcome.Completed);
            }
            activity.Complete(HostOperationOutcome.Completed);
            return new(archived, deleted, held, candidates.Count > MaximumRetentionSessions);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private List<HostId<SessionIdentity>> ReadRetentionCandidates(SqliteConnection connection, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.session_id FROM session_retention r JOIN work_sessions s ON s.session_id=r.session_id
            WHERE r.perpetual=0 AND r.purged=0
            ORDER BY (EXISTS(SELECT 1 FROM host_tasks t WHERE t.session_id=r.session_id AND t.state IN (0,1,7))
                OR EXISTS(SELECT 1 FROM host_questions q WHERE q.session_id=r.session_id AND json_extract(q.payload,'$.Status')=0)),
                r.last_activity,r.session_id;
            """;
        var candidates = new List<HostId<SessionIdentity>>();
        var controls = new List<HostId<SessionIdentity>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            var id = new HostId<SessionIdentity>(ParseId(reader.GetString(0)));
            var clock = ReadRetention(connection, id);
            var state = RequireSession(connection, id);
            var now = time.GetUtcNow();
            if (clock.DeleteDue <= now || state.State == 0 && clock.ArchiveDue <= now)
            {
                // Current-run control authorities must not crowd idle content out of the bounded batch.
                if (liveControlSessions.Contains(id))
                {
                    if (controls.Count <= MaximumRetentionSessions) { controls.Add(id); }
                    continue;
                }
                candidates.Add(id);
                if (candidates.Count > MaximumRetentionSessions) { break; }
            }
        }
        candidates.AddRange(controls.Take(MaximumRetentionSessions + 1 - candidates.Count));
        return candidates;
    }

    private void CompleteDeletion(SqliteConnection connection, HostId<SessionIdentity> id, Func<bool> admitted)
    {
        using var completion = connection.BeginTransaction();
        Execute(connection, completion, "UPDATE session_retention SET purged=1 WHERE session_id=$id;", ("$id", Id(id)));
        // A committed revocation is not undone by cancellation; recovery retries incomplete inventories.
        Commit(completion, HostActivity.RequireCurrent().Request, CancellationToken.None, admitted);
        database.RequireEmptyJournal();
    }

    private bool HasRetentionHold(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM host_tasks WHERE session_id=$id AND state IN (0,1,7) LIMIT 1;";
        command.Parameters.AddWithValue("$id", Id(session));
        return liveControlSessions.Contains(session) || command.ExecuteScalar() is not null
            || ReadQuestions(connection, session).Any(q => q.Status == QuestionStatus.Pending);
    }

    internal static void RequireArtifactSources(SqliteConnection connection, ArtifactIdentity identity)
    {
        ValidateAuthority(connection);
        var source = RequireSession(connection, identity.SessionId);
        var owner = RequireSession(connection, identity.DeletionOwnerId);
        if (source.State != 0 || owner.State == 2)
        {
            throw new InvalidOperationException("Artifact publication is revoked for an inactive source or removed deletion owner.");
        }
    }

    private async Task DeleteArtifactsAsync(HostId<SessionIdentity> session, Func<Task> rewrite, CancellationToken token)
    {
        var artifacts = new RestrictedStorageDirectory(paths);
        if (!artifacts.HasExistingPartition())
        {
            await rewrite().ConfigureAwait(false);
            return;
        }
        artifacts.Verify();
        foreach (var entry in Directory.EnumerateFileSystemEntries(artifacts.Root))
        {
            if (!Same(entry, artifacts.Keys) && !Same(entry, artifacts.Artifacts)
                && !Same(Path.GetFileName(entry), "operation.lock"))
            {
                throw new InvalidDataException("An uninventoried managed backup or storage artifact holds session deletion.");
            }
        }
        var keyFiles = Directory.EnumerateFileSystemEntries(artifacts.Keys).ToArray();
        if (keyFiles.Length != 1 || !Same(Path.GetFileName(keyFiles[0]), WindowsStorageKeyStore.PublishedFileName))
        {
            throw new InvalidDataException("Uncertain key publication holds deletion; retention does not publish staging.");
        }
        using var key = await new WindowsStorageKeyStore(paths).OpenAsync(token).ConfigureAwait(false);
        await new WindowsEncryptedArtifactStore(paths, key).DeleteOwnedAsync(session, rewrite, token).ConfigureAwait(false);
    }

    private void PurgeOrArchive(SqliteConnection connection, HostId<SessionIdentity> id, bool deleting,
        Func<bool> admitted, CancellationToken token)
    {
        using (var settings = connection.CreateCommand())
        {
            // Secure-delete clears deleted cell/index/free-page content; PERSIST truncation owns the rollback copy.
            settings.CommandText = "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;";
            settings.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        var row = RequireSession(connection, id);
        var session = new WorkSessionAuthorization(id, new(checked(row.Authority.Generation.Value + 1)), false);
        var request = HostActivity.RequireCurrent().Request;
        var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        var audit = new SecurityAuditEvent(Guid.NewGuid(),
            SecurityAuditCategory.SecurityApproval, deleting ? "session.retention.delete" : "session.retention.archive",
            SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.System, Id(request.TaskId));
        using var auditActivity = HostActivity.BeginAudit(request, audit);
        var grants = ReadGrants(connection).Where(g => g.Scope != OperationGrantScope.Perpetual
            && g.ApprovedProposal.Request.SessionId == id && g.Status == OperationGrantStatus.Active)
            .Select(g => EndGrant(g, "work-session-ended")).ToArray();
        var changes = new List<AuthorityChange> { SessionChange(session, deleting ? 2 : 1) };
        changes.AddRange(grants.Select(GrantChange));
        var memories = MemoryInvalidations(connection, id);
        changes.AddRange(memories.Select(MemoryChange));
        var sequence = AppendAudit(connection, transaction, intent, session, audit, changes: changes);
        WriteSession(connection, transaction, session, deleting ? 2 : 1, sequence);
        foreach (var memory in memories) { WriteMemory(connection, transaction, memory, sequence); }
        foreach (var grant in grants) { WriteGrant(connection, transaction, grant, sequence); }
        Execute(connection, transaction, "DELETE FROM host_observations WHERE session_id=$id;", ("$id", Id(id)));
        if (deleting)
        {
            Execute(connection, transaction, """
                DELETE FROM host_task_waits WHERE task_id IN (SELECT task_id FROM host_tasks WHERE session_id=$id);
                DELETE FROM session_queue WHERE session_id=$id;
                DELETE FROM host_task_runs WHERE task_id IN (SELECT task_id FROM host_tasks WHERE session_id=$id);
                DELETE FROM host_task_events WHERE task_id IN (SELECT task_id FROM host_tasks WHERE session_id=$id);
                DELETE FROM host_tasks WHERE session_id=$id;
                DELETE FROM host_questions WHERE session_id=$id;
                DELETE FROM scoped_grants WHERE session_id=$id;
                DELETE FROM session_metadata WHERE session_id=$id;
                DELETE FROM session_history WHERE session_id=$id;
                DELETE FROM session_history_heads WHERE session_id=$id;
                """, ("$id", Id(id)));
            // Keep only a content-free redacted gap and exact-ID authority tombstone to reject late appends.
            SessionHistoryPersistence.Seed(connection, transaction, id, session.Generation, baseline: false);
            SessionHistoryPersistence.Redact(connection, transaction, id);
        }
        Commit(transaction, request, token, admitted);
        database.VerifyFiles();
        database.RequireEmptyJournal();
        auditActivity.Complete(HostOperationOutcome.Completed);
    }
}
