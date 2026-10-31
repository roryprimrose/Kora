using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionRetentionApplyStore
{
    public ValueTask<SessionRetentionApplyReview> ReviewRetentionApplyAsync(
        HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = database.OpenReadOnly(token);
            var policy = sessionRetentionPolicy.Observe();
            var before = ReadRetentionObservation(connection, session, metadataOnly: true);
            RequireRetentionApplicationIdle(connection, session, null);
            if (before.Removed || before.State.Purged || before.State.Perpetual)
            { throw new InvalidOperationException("Apply-now requires one existing ordinary, nonremoved conversation."); }
            database.RequireEmptyArtifactInventory();
            database.RequireCommittedJournal();
            var cutoff = time.GetUtcNow();
            var after = SessionRetentionApplication.Recalculate(before.State, policy.Settings, cutoff);
            var target = RequireDispositionTarget(connection, session, before.Generation,
                ReadMetadata(connection, session)?.Revision.Value ?? 0);
            var head = RetentionApplicationHead(connection);
            var id = Guid.NewGuid();
            return new SessionRetentionApplyReview(id, before, after, policy.Settings, policy.Revision,
                DescribeDispositionInventory(connection, target, id, head), cutoff, head, database.ReadPreviewStamp());
        }, token));
    }

    public ValueTask ValidateRetentionApplyAsync(SessionRetentionApplyReview review, CancellationToken token) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = database.OpenReadOnly(token);
            RequireRetentionApplicationCurrent(connection, review, null);
            if (!Same(review.Snapshot, database.ReadPreviewStamp()))
            { throw new InvalidOperationException("The reviewed storage source changed. Review again; no retry or clock renewal."); }
        }, token));

    public async ValueTask<SessionRetentionApplyReceipt> ApplyReviewedRetentionAsync(HostRequest request,
        SessionRetentionApplyReview review, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(admitted);
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
            || request.SessionId != review.Before.State.SessionId)
        { throw new InvalidOperationException("Apply-now requires original native authority for the exact reviewed conversation."); }
        bool Current() => admitted() && RetentionApplicationPolicyCurrent(review);
        try
        {
            switch (review.Effect)
            {
                case SessionRetentionApplyEffect.Delete:
                    await DisposeSessionAsync(request, review.Inventory, Current, review, token).ConfigureAwait(false);
                    break;
                case SessionRetentionApplyEffect.Archive:
                    await SetLifecycleAsync(request, review.Before.Generation, active: false, remove: false,
                        Current, token, review).ConfigureAwait(false);
                    break;
                default:
                    await RunHostMutationAsync(request, "session.retention.apply-now", (connection, transaction, intent, audit) =>
                    {
                        ApplyReviewedRetentionPolicy(connection, transaction, intent, audit, review);
                        WindowsSqliteHostTaskStore.WriteTask(connection, transaction, intent.Next(HostTaskState.Succeeded));
                        CompleteRetentionApplication(connection, transaction, intent,
                            RequireSession(connection, request.SessionId).Authority, audit);
                        return true;
                    }, token, Current).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Only confirmed precommit cancellation is terminalised here; COMMIT uncertainty is IOException.
            await RunHostMutationAsync(request, "session.retention.apply-now.cancelled",
                (connection, transaction, intent, audit) =>
            {
                RequireFreshMetadataIntent(intent);
                var cancelled = intent.Next(HostTaskState.Cancelled);
                AppendAudit(connection, transaction, intent, RequireSession(connection, request.SessionId).Authority,
                    audit.WithOutcome(SecurityAuditOutcome.Cancelled), changes: [TaskChange(cancelled)]);
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, cancelled);
                return true;
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        // All effects, including the terminal control receipt, committed together. Cancellation now cannot undo them.
        return await Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(CancellationToken.None);
            using var connection = Open(created: false, CancellationToken.None);
            var actual = ReadRetentionObservation(connection, request.SessionId);
            var generation = review.Before.Generation.Value + (review.Effect == SessionRetentionApplyEffect.DeadlinesOnly ? 0 : 1);
            var task = ReadTask(connection, request.TaskId);
            if (!Current() || actual.State != review.After || actual.Generation.Value != generation
                || actual.Removed != (review.Effect == SessionRetentionApplyEffect.Delete)
                || task?.Request != request || task.State != HostTaskState.Succeeded
                || task.Revision.Value != 2 || !HasRetentionApplicationReceipt(connection, request))
            { throw new IOException("Retention application committed but exact audit/terminal/source readback is unconfirmed. Inspect durable state; no rollback or replay."); }
            return new SessionRetentionApplyReceipt(review, actual, review.Effect);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private bool RetentionApplicationPolicyCurrent(SessionRetentionApplyReview review)
    {
        var policy = sessionRetentionPolicy.Observe();
        var now = time.GetUtcNow();
        return policy.Settings == review.Settings && policy.Revision == review.PolicyRevision
            && now >= review.Cutoff && now < review.ExpiresAt
            && SessionRetentionApplication.Effect(review.After, review.Inventory.Session.Authority.IsActive, now) == review.Effect;
    }

    private void RequireRetentionApplicationCurrent(SqliteConnection connection,
        SessionRetentionApplyReview review, HostId<TaskIdentity>? control)
    {
        var session = review.Before.State.SessionId;
        var current = ReadRetentionObservation(connection, session, metadataOnly: true);
        RequireRetentionApplicationIdle(connection, session, control);
        database.RequireEmptyArtifactInventory();
        database.RequireCommittedJournal();
        var target = RequireDispositionTarget(connection, session, review.Before.Generation,
            review.Inventory.Session.Metadata?.Revision.Value ?? 0);
        if (review.Id == Guid.Empty || review.Before.Removed || review.Before.State.Purged || review.Before.State.Perpetual
            || !current.MatchesRecordedState(review.Before) || !RetentionApplicationPolicyCurrent(review)
            || review.After != SessionRetentionApplication.Recalculate(current.State, review.Settings, review.Cutoff)
            || !Same(review.SourceHead, RetentionApplicationHead(connection))
            || review.Inventory != DescribeDispositionInventory(connection, target, review.Id, review.SourceHead))
        { throw new InvalidOperationException("Reviewed policy, source head, inventory, clock or generation changed. Obtain a fresh explicit review."); }
    }

    private void RequireRetentionApplicationIdle(SqliteConnection connection, HostId<SessionIdentity> session,
        HostId<TaskIdentity>? control)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM host_tasks WHERE session_id=$id AND task_id<>$control AND state NOT IN (2,3,4,5,6)
            UNION ALL SELECT 1 FROM host_questions WHERE session_id=$id
                AND coalesce(json_extract(payload,'$.Status'),-1) NOT IN (1,2,3)
            UNION ALL SELECT 1 FROM session_queue WHERE session_id=$id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", Id(session));
        command.Parameters.AddWithValue("$control", control is { } task ? Id(task) : string.Empty);
        if (liveControlSessions.Contains(session) || command.ExecuteScalar() is not null)
        { throw new InvalidOperationException("Live, queued, Unknown/unavailable work, unresolved questions or held control authority excludes apply-now. Nothing was stopped."); }
    }

    private string RetentionApplicationHead(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,hash FROM authority_head WHERE singleton=1;";
        using var row = command.ExecuteReader();
        if (!row.Read()) { throw new InvalidDataException("The retention source head is unavailable."); }
        return RequireInspectionIdentity() + ":" + row.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + row.GetString(1);
    }

    private void ApplyReviewedRetentionPolicy(SqliteConnection connection, SqliteTransaction transaction,
        HostTaskRecord intent, SecurityAuditEvent audit, SessionRetentionApplyReview review)
    {
        RequireFreshMetadataIntent(intent);
        RequireRetentionApplicationCurrent(connection, review, intent.Request.TaskId);
        var requested = RetentionApplicationAudit(audit, SecurityAuditOutcome.Requested);
        using (var activity = HostActivity.BeginAudit(intent.Request, requested))
        {
            AppendAudit(connection, transaction, intent, RequireSession(connection, intent.Request.SessionId).Authority, requested);
            activity.Complete(HostOperationOutcome.Completed);
        }
        Execute(connection, transaction,
            "UPDATE session_retention SET archive_days=$archive,delete_days=$delete WHERE session_id=$id;",
            ("$archive", review.Settings.ArchiveDays), ("$delete", review.Settings.DeleteDays),
            ("$id", Id(intent.Request.SessionId)));
    }

    private void CompleteRetentionApplication(SqliteConnection connection, SqliteTransaction transaction,
        HostTaskRecord intent, Kora.Core.Authorization.WorkSessionAuthorization session, SecurityAuditEvent audit)
    {
        var terminal = RetentionApplicationAudit(audit, SecurityAuditOutcome.Succeeded);
        using var activity = HostActivity.BeginAudit(intent.Request, terminal);
        AppendAudit(connection, transaction, intent, session, terminal,
            changes: [TaskChange(intent.Next(HostTaskState.Succeeded))]);
        activity.Complete(HostOperationOutcome.Completed);
    }

    private static SecurityAuditEvent RetentionApplicationAudit(SecurityAuditEvent audit, SecurityAuditOutcome outcome) =>
        new(Guid.NewGuid(), audit.Category, "session.retention.apply-now", outcome, audit.Initiator, audit.TargetId);

    private static bool HasRetentionApplicationReceipt(SqliteConnection connection, HostRequest request)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM security_audit_events WHERE json_extract(envelope,'$.Request.RequestId.Value')=$request
                AND json_extract(envelope,'$.Audit.ActionId')='session.retention.apply-now'
                AND json_extract(envelope,'$.Audit.Outcome') IN (0,1)
                AND (SELECT max(sequence) FROM security_audit_events
                    WHERE json_extract(envelope,'$.Request.RequestId.Value')=$request)
                    =(SELECT sequence FROM authority_head WHERE singleton=1);
            """;
        command.Parameters.AddWithValue("$request", Id(request.RequestId));
        return command.ExecuteScalar() is long count && count == 2;
    }
}
