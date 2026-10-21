using Kora.Core.Context;
using Kora.Core.Diagnostics;

namespace Kora.Tools.Clipboard;

public sealed partial class ClipboardSnapshotBroker
{
    internal void PublishStatus(Guid snapshotId, Func<bool> canPresent,
        Action<ClipboardSnapshotStatusResult> publish, CancellationToken cancellationToken)
    {
        var request = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        lock (sync)
        {
            var original = snapshot;
            var sourceEligible = eligible;
            var admittedGeneration = generation;
            // Do not call Current: status is observation, not source retirement or renewal.
            var allowed = ClipboardCommand.IsDeliberateOrigin(request.Origin) && request.InvocationId is null
                && canPresent() && sourceEligible is not null && sourceEligible();
            var result = allowed && !disposed && !cancellationToken.IsCancellationRequested
                && HostActivity.Current?.Request == request
                && ReferenceEquals(original, snapshot) && admittedGeneration == generation
                ? ClipboardSnapshotStatusResult.FromCurrent(snapshotId, original)
                : ClipboardSnapshotStatusResult.Unavailable;
            publish(result);
            StatusReported(logger, result.IsPresent);
            activity.Complete(result.IsPresent ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        }
    }
}
