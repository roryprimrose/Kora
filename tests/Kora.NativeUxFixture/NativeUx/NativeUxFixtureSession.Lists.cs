using System.Diagnostics;

using Kora.Application.Interaction;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.NativeUxFixture;

internal sealed partial class NativeUxFixtureSession
{
    internal bool ListOverflow { get; }
    internal LocalEventBroker? OverflowEvents { get; private set; }
    internal TraceSnapshot? OverflowTrace { get; private set; }
    internal IReadOnlyList<string> OverflowLinks { get; private set; } = [];

    private async Task InitializeListOverflowAsync()
    {
        var target = LifecycleTarget ?? throw new InvalidOperationException("The exact overflow subject is unavailable.");
        for (var index = 0; index < 10; index++)
        {
            var snapshot = await Interactions.ReadQueueAsync(target.SessionId, Token);
            await Sessions.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, target.SessionId.Value, target.Generation.Value)
            {
                QueueRevision = snapshot.Revision,
                WorkRequestId = Guid.NewGuid(),
                TaskId = Guid.NewGuid(),
            }, RequestOrigin.LocalUi, () => Access.CanControl, Token);
            if (index < 9)
            {
                var pending = await Interactions.ReadQueueAsync(target.SessionId, Token);
                var dispatched = await Sessions.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch,
                    target.SessionId.Value, target.Generation.Value) { QueueRevision = pending.Revision },
                    RequestOrigin.LocalUi, () => Access.CanControl, Token);
                if (dispatched.QueueDispatch is not { Count: 1 } receipts
                    || receipts[0].Entry.State != SessionQueueState.Succeeded)
                {
                    throw new InvalidOperationException("The synthetic local-version overflow receipt did not commit successfully.");
                }
            }
        }
        var notifyOnly = CreateMaintenance();
        OverflowEvents = new(new AuthorityLocalEventSource(Interactions, Access, notifyOnly, new(), TimeProvider.System),
            new LocalEventStateStore(this), Access, TimeProvider.System, Audit, NullLogger<LocalEventBroker>.Instance);
        var links = Enumerable.Range(0, 12).Select(_ =>
            new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded)).ToArray();
        using var linked = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request, links.Select(context => new ActivityLink(context)).ToArray());
        OverflowTrace = TraceSnapshot.Capture(linked.Activity)
            ?? throw new InvalidOperationException("The synthetic overflow trace was not sampled.");
        OverflowLinks = links.Select(context => context.TraceId.ToHexString()).ToArray();
        linked.Complete(HostOperationOutcome.Completed);
    }
}
