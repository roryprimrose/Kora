using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public Func<bool> CaptureEvidenceAnswerAdmission()
    {
        var revision = CallPolicyRevision;
        var input = HostActivity.Current;
        var original = !HostActivity.HasScope || input is { Outcome: Kora.Core.Diagnostics.HostOperationOutcome.Unknown }
            && !input.Activity!.IsStopped && input.Request.Origin == RequestOrigin.LocalUi && input.Request.InvocationId is null;
        return () => original && IsClipboardEligible(RequestOrigin.LocalUi, revision) && CanRevealPrivatePresentation;
    }
}
