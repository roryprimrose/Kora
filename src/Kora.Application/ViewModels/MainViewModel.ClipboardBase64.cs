using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public ClipboardBase64Inspection InspectClipboardBase64(ClipboardSnapshot snapshot, CancellationToken token)
    {
        var revision = CallPolicyRevision;
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        try
        {
            var result = new ClipboardInspectBase64(clipboardPreview).Execute(snapshot,
                () => IsClipboardEligible(RequestOrigin.LocalUi, revision), token);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            ClipboardBase64Failed(logger, exception.GetType().Name);
            throw;
        }
    }
}
