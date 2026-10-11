using System.Collections.Immutable;

using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private void MigrateQueueOrder(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        ValidateQueue(connection);
        ValidateRetention(connection);
        ValidateMemory(connection);
        ValidateAttachments(connection);
        ValidateProviderProfiles(connection);
        using var guard = connection.CreateCommand();
        guard.Transaction = transaction;
        guard.CommandText = """
            SELECT 1 FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='queue-order' LIMIT 1;
            """;
        if (guard.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Missing committed queue order cannot be migrated as default FIFO.");
        }
        Execute(connection, transaction, HostInteractionSchema.QueueOrderTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<SessionQueueSnapshot> MovePendingAsync(HostRequest control, SessionQueueMoveReview review,
        Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(control, review.Up ? "queue.move-up" : "queue.move-down",
            (connection, transaction, intent, audit) =>
            {
                RequireFreshMetadataIntent(intent);
                var expected = review.Queue;
                var snapshot = RequireQueueRevision(connection, control, expected.Generation, expected.Revision);
                var previous = QueueControl(connection, control.SessionId);
                if (control.SessionId != expected.SessionId || control.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                    || control.InvocationId is not null || expected.DispatchControl != previous || previous.Held
                    || previous.Revision > 0 && previous.ControlRevision != review.AdmissionRevision
                    || previous.RunId != runId || !eligible())
                {
                    throw new InvalidOperationException("Refresh and review under the exact unchanged current-run private queue control.");
                }
                var selected = snapshot.Entries.SingleOrDefault(entry => entry.Request.TaskId == review.Selected.Request.TaskId);
                var neighbor = snapshot.Entries.SingleOrDefault(entry => entry.Request.TaskId == review.Neighbor.Request.TaskId);
                if (selected != review.Selected || neighbor != review.Neighbor)
                {
                    throw new InvalidOperationException("The exact reviewed entry identities or revisions changed. Refresh and review again.");
                }
                var pending = SessionQueueOrdering.Pending(snapshot.Entries, control.SessionId, snapshot.Orders);
                var refusal = SessionQueueOrdering.Refusal(pending, selected!, neighbor!, review.Up,
                    runId, expected.Generation, review.AdmissionRevision, time.GetUtcNow());
                if (refusal is not null) { throw new InvalidOperationException(refusal); }
                var oldOrder = snapshot.Orders.SingleOrDefault(order => order.RunId == runId && order.Generation == expected.Generation);
                var ids = pending.Select(entry => entry.Request.TaskId).ToArray();
                var first = Array.IndexOf(ids, selected!.Request.TaskId);
                var second = Array.IndexOf(ids, neighbor!.Request.TaskId);
                (ids[first], ids[second]) = (ids[second], ids[first]);
                var nextOrder = oldOrder is null
                    ? new SessionQueueOrder(1, Guid.NewGuid(), control.SessionId, expected.Generation, runId, 1, [.. ids])
                    : oldOrder with { Revision = checked(oldOrder.Revision + 1), Tasks = [.. ids] };
                nextOrder.Validate(ReadQueueRows(connection, control.SessionId));
                var nextControl = previous with { Revision = checked(previous.Revision + 1), ControlRevision = review.AdmissionRevision };
                // Uncertain COMMIT or delivery must leave a real run-only admission hold, never an optimistic reordered list.
                queueControls[control.SessionId] = nextControl with { Held = true };
                var terminal = intent.Next(HostTaskState.Succeeded);
                var receipt = SessionQueueMoveReceipt.FromReview(review);
                receipt.Validate();
                var sequence = AppendAudit(connection, transaction, intent, RequireSession(connection, control.SessionId).Authority,
                    audit, changes: [QueueOrderChange(nextOrder), TaskChange(terminal)],
                    requestedAudit: audit.WithOutcome(SecurityAuditOutcome.Requested), queueMoveReceipt: receipt);
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal);
                WriteQueueOrder(connection, transaction, nextOrder, sequence);
                return QueueSnapshot(connection, control.SessionId) with { DispatchControl = nextControl, MoveReceipt = receipt };
            }, token, () => CanCommitMove(review, eligible), requireIdle: false,
            committed: snapshot => queueControls[control.SessionId] = snapshot.DispatchControl!);

    private bool CanCommitMove(SessionQueueMoveReview review, Func<bool> eligible)
    {
        // The lease holds identities/order throughout the transaction; time and private admission still change independently.
        var pair = review.Up ? new[] { review.Neighbor, review.Selected } : new[] { review.Selected, review.Neighbor };
        return eligible() && SessionQueueOrdering.Refusal(pair, review.Selected, review.Neighbor, review.Up,
            runId, review.Queue.Generation, review.AdmissionRevision, time.GetUtcNow()) is null;
    }

    private static AuthorityChange QueueOrderChange(SessionQueueOrder order) =>
        Change("queue-order", order.Id.ToString("D"), order.Revision, HostInteractionCodec.Encode(order));

    private static void WriteQueueOrder(SqliteConnection connection, SqliteTransaction transaction, SessionQueueOrder? order, long sequence)
    {
        if (order is null) { return; }
        Execute(connection, transaction, """
            INSERT INTO session_queue_order VALUES($id,$session,$run,$generation,$revision,$payload,$audit)
            ON CONFLICT(order_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
            """, ("$id", order.Id.ToString("D")), ("$session", Id(order.SessionId)),
            ("$run", order.RunId.ToString("D")), ("$generation", order.Generation.Value), ("$revision", order.Revision),
            ("$payload", HostInteractionCodec.Encode(order)), ("$audit", sequence));
    }

    private static SessionQueueOrder? UpdatedQueueOrder(SqliteConnection connection, HostId<SessionIdentity> session,
        Guid run, HostRevision generation, Func<ImmutableArray<HostId<TaskIdentity>>, ImmutableArray<HostId<TaskIdentity>>> update)
    {
        var current = ReadQueueOrders(connection).SingleOrDefault(order => order.SessionId == session
            && order.RunId == run && order.Generation == generation);
        return current is null ? null : current with { Revision = checked(current.Revision + 1), Tasks = update(current.Tasks) };
    }

    private static IReadOnlyList<AuthorityChange> QueueOrderChanges(SessionQueueOrder? order) =>
        order is null ? [] : [QueueOrderChange(order)];

    private static List<SessionQueueOrder> ReadQueueOrders(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_id,session_id,run_id,generation,revision,payload,audit_sequence FROM session_queue_order ORDER BY audit_sequence;";
        using var reader = command.ExecuteReader();
        var orders = new List<SessionQueueOrder>();
        var entries = ReadQueueRows(connection);
        while (reader.Read())
        {
            var payload = reader.GetString(5);
            var order = HostInteractionCodec.Decode<SessionQueueOrder>(payload);
            order.Validate(entries);
            if (!Same(HostInteractionCodec.Encode(order), payload) || !Same(order.Id.ToString("D"), reader.GetString(0))
                || !Same(Id(order.SessionId), reader.GetString(1)) || !Same(order.RunId.ToString("D"), reader.GetString(2))
                || order.Generation.Value != reader.GetInt64(3) || order.Revision != reader.GetInt64(4))
            {
                throw new InvalidDataException("The queue order row conflicts with its exact canonical lineage.");
            }
            ValidateRowAuthority(connection, reader.GetInt64(6), QueueOrderChange(order));
            orders.Add(order);
        }
        return orders;
    }

    private static void ValidateQueueOrders(SqliteConnection connection)
    {
        _ = ReadQueueOrders(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id,
                    json_extract(a.envelope,'$.Request.SessionId.Value') AS session_id, MAX(a.sequence) AS sequence
                FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='queue-order' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN session_queue_order q ON q.order_id=latest.id
            LEFT JOIN work_sessions s ON s.session_id=latest.session_id
            LEFT JOIN security_audit_events deletion ON deletion.sequence=s.audit_sequence
            WHERE s.state IS NULL OR (q.audit_sequence IS NULL AND
                (s.state<>2 OR json_extract(deletion.envelope,'$.Audit.ActionId')<>'session.retention.delete'))
                OR (q.audit_sequence IS NOT NULL AND q.audit_sequence<>latest.sequence) LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Committed queue order is missing or stale; default FIFO substitution is forbidden.");
        }
        command.CommandText = """
            SELECT 1 FROM session_queue_order q JOIN session_retention r ON r.session_id=q.session_id
            WHERE r.purged=1 LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Purged session retains queue order content.");
        }
    }
}
