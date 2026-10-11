using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    // All access occurs under the existing exclusive task/database lease, including the physical dispatch COMMIT.
    // No dispatch batch or callback holds this fence. Nothing in this map is restart authority.
    private readonly Dictionary<HostId<SessionIdentity>, SessionQueueDispatchControl> queueControls = [];

    public ValueTask HoldDispatchControlAsync(SessionQueueSnapshot expected, CancellationToken token) => new(Task.Run(() =>
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = Open(created: false, token);
        if (expected.DispatchControl is { } receipt && receipt.RunId == runId
            && RequireSession(connection, expected.SessionId).Authority.Generation == receipt.Generation
            && queueControls.TryGetValue(expected.SessionId, out var current) && current == receipt)
        {
            queueControls[expected.SessionId] = current with { Held = true };
        }
        database.VerifyFiles();
    }, token));

    private SessionQueueDispatchControl QueueControl(SqliteConnection connection, HostId<SessionIdentity> session,
        long? admissionRevision = null)
    {
        var generation = RequireSession(connection, session).Authority.Generation;
        if (!queueControls.TryGetValue(session, out var current) || current.Generation != generation)
        {
            current = new(runId, generation, 0, admissionRevision ?? 0, false, false);
        }
        return admissionRevision is { } revision && current.Revision > 0 && revision != current.ControlRevision
            ? current with { Held = true } : current;
    }

    public ValueTask<SessionQueueSnapshot> SetDispatchPausedAsync(HostRequest control, HostRevision generation,
        long expectedQueueRevision, Guid expectedRunId, long expectedControlRevision, long admissionRevision,
        bool paused, Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(control, paused ? "queue.pause" : "queue.resume",
            (connection, transaction, intent, audit) =>
            {
                RequireFreshMetadataIntent(intent);
                _ = RequireQueueRevision(connection, control, generation, expectedQueueRevision);
                var previous = QueueControl(connection, control.SessionId, admissionRevision);
                if (control.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                    || control.InvocationId is not null || expectedRunId != runId
                    || expectedControlRevision != previous.Revision || admissionRevision < previous.ControlRevision
                    || !paused && previous.Revision > 0 && previous.ControlRevision != admissionRevision
                    || !eligible())
                {
                    throw new InvalidOperationException("Refresh the exact current-run pause receipt and unchanged private session authority.");
                }
                var next = new SessionQueueDispatchControl(runId, generation, checked(previous.Revision + 1),
                    admissionRevision, paused, false);
                // Once a valid control begins, audit/cancellation/storage uncertainty must not silently unpause.
                queueControls[control.SessionId] = next with { Paused = previous.Paused || paused, Held = true };
                var authority = RequireSession(connection, control.SessionId).Authority;
                var terminal = intent.Next(HostTaskState.Succeeded);
                AppendAudit(connection, transaction, intent, authority, audit, changes: [TaskChange(terminal)],
                    requestedAudit: audit.WithOutcome(SecurityAuditOutcome.Requested));
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal);
                return QueueSnapshot(connection, control.SessionId) with { DispatchControl = next };
            }, token, eligible, requireIdle: false,
            committed: snapshot => queueControls[control.SessionId] = snapshot.DispatchControl!);
}
