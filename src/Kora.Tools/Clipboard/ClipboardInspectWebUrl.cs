using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Network;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Clipboard;

/// <summary>Native-only local syntax inspection; not registered as a model or command action.</summary>
public sealed partial class ClipboardInspectWebUrl(
    ClipboardSnapshotBroker snapshots, ILogger<ClipboardInspectWebUrl> logger)
{
    private int running;
    private long generation;

    public WebUrlSyntaxResult Execute(ClipboardSnapshot source, Func<bool> eligible, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            Interlocked.Increment(ref generation);
            return new(WebUrlSyntaxOutcome.Busy);
        }
        var admittedGeneration = Interlocked.Increment(ref generation);
        using var activity = HostActivity.BeginRoot(source.Request, HostActivityLayer.Application, HostOperation.Policy);
        try
        {
            bool Current() => !token.IsCancellationRequested && eligible() && ReferenceEquals(snapshots.Current, source)
                && !token.IsCancellationRequested && admittedGeneration == Interlocked.Read(ref generation);
            WebUrlSyntaxResult Lost()
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return new(token.IsCancellationRequested ? WebUrlSyntaxOutcome.Cancelled : WebUrlSyntaxOutcome.Stale);
            }
            if (!Current()) { return Lost(); }
            var result = WebPageAddressSyntax.InspectExact(source.Text);
            if (!Current()) { return Lost(); }
            activity.Complete(result.Outcome == WebUrlSyntaxOutcome.Accepted ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            InspectionFailed(logger);
            activity.Complete(HostOperationOutcome.Failed);
            return new(WebUrlSyntaxOutcome.Failed);
        }
        finally { Volatile.Write(ref running, 0); }
    }
}
