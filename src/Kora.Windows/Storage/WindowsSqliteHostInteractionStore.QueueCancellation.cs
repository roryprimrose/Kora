using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<SessionQueueSnapshot> CancelSelectedPendingAsync(HostRequest control, SessionQueueCancelReview review,
        Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(control, "queue.cancel-selected", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            var expected = review.Queue;
            var snapshot = RequireQueueRevision(connection, control, expected.Generation, expected.Revision);
            var previous = QueueControl(connection, control.SessionId, review.AdmissionRevision);
            if (control.SessionId != expected.SessionId || control.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                || control.InvocationId is not null || expected.DispatchControl != previous
                || previous.RunId != runId || !eligible())
            {
                throw new InvalidOperationException("Refresh and review the complete subset under unchanged exact current-run private control.");
            }
            var members = SessionQueueCancellation.Select(snapshot,
                [.. review.Entries.Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision))]);
            if (!members.SequenceEqual(review.Entries))
            {
                throw new InvalidOperationException("One reviewed member changed; the entire cancellation is refused.");
            }
            var receipt = SessionQueueCancelReceipt.FromReview(review);
            receipt.Validate();
            var terminal = intent.Next(HostTaskState.Succeeded);
            // Audit/COMMIT uncertainty must hold admission. A known successful commit restores the exact previous pause receipt.
            queueControls[control.SessionId] = previous with { Held = true };
            var result = WritePendingRemoval(connection, transaction, intent, audit, members.ToArray(),
                SessionQueueState.Cancelled, terminal, receipt);
            return result with { DispatchControl = previous, CancelReceipt = receipt };
        }, token, eligible, requireIdle: false,
        committed: snapshot => queueControls[control.SessionId] = snapshot.DispatchControl!);
}
