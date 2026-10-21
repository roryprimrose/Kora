using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardCountText(ClipboardSnapshotBroker broker)
{
    public ClipboardTextStatisticsResult Execute(ClipboardSnapshot snapshot, Func<bool> canPresent,
        CancellationToken cancellationToken)
    {
        var request = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool IsCurrent() => request.Origin == RequestOrigin.LocalUi && request.InvocationId is null
                && canPresent() && ReferenceEquals(broker.Current, snapshot)
                && canPresent() && ReferenceEquals(broker.Current, snapshot);
            if (!IsCurrent())
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return new(snapshot, null);
            }
            var statistics = ClipboardTextStatistics.Measure(snapshot, cancellationToken);
            var current = IsCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            activity.Complete(current ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            return new(snapshot, current ? statistics : null);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception)
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
