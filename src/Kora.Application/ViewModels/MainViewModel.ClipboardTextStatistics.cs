using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public ClipboardTextStatisticsResult CountClipboardText(ClipboardSnapshot snapshot, Func<bool> nativeCurrent,
        CancellationToken cancellationToken)
    {
        using var activity = HostActivity.BeginRoot(HostActivity.Current?.Request ?? HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        try
        {
            var admitted = CaptureClipboardJsonEligibility(snapshot);
            var result = new ClipboardCountText(clipboardPreview).Execute(snapshot,
                () => admitted() && nativeCurrent(), cancellationToken);
            activity.Complete(result.Statistics is null ? HostOperationOutcome.Cancelled : HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            activity.Complete(HostOperationOutcome.Failed);
            ClipboardTextStatisticsFailed(logger, exception.GetType().Name);
#pragma warning disable MA0054 // Source-bearing callback details must not cross the native presentation boundary.
            throw new InvalidOperationException("Native clipboard text counting failed internally. No counts are claimed.");
#pragma warning restore MA0054
        }
    }
}
