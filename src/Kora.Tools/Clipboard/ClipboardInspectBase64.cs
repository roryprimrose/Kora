using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardInspectBase64(ClipboardSnapshotBroker broker)
{
    public ClipboardBase64Inspection Execute(ClipboardSnapshot snapshot, Func<bool> eligible, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        try
        {
            EnsureCurrent();
            var result = ClipboardBase64Policy.Inspect(snapshot.Text, token);
            EnsureCurrent();
            activity.Complete(HostOperationOutcome.Completed);
            return result;
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

        void EnsureCurrent()
        {
            token.ThrowIfCancellationRequested();
            if (HostActivity.RequireCurrent().Request.Origin != RequestOrigin.LocalUi
                || !eligible() || !ReferenceEquals(broker.Current, snapshot))
            {
                throw new OperationCanceledException("Exact native clipboard inspection is no longer admitted.");
            }
            token.ThrowIfCancellationRequested();
        }
    }
}
