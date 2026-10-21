using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public ClipboardXmlValidationResult ValidateClipboardXml(ClipboardSnapshot snapshot, Func<bool> nativeCurrent,
        CancellationToken cancellationToken)
    {
        var revision = CallPolicyRevision;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Tool);
        try
        {
            var result = new ClipboardValidateXml(clipboardPreview).Execute(snapshot,
                () => IsClipboardEligible(RequestOrigin.LocalUi, revision) && nativeCurrent(), cancellationToken);
            activity.Complete(result.Outcome == ClipboardXmlOutcome.WellFormedXml
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
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
            ClipboardXmlFailed(logger, exception.GetType().Name);
#pragma warning disable MA0054 // Source-bearing exception details must not cross the native presentation boundary.
            throw new InvalidOperationException("Native XML well-formedness checking failed internally. No XML result is claimed.");
#pragma warning restore MA0054
        }
    }
}
