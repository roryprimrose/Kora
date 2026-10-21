using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public Func<bool> CaptureLocalFilePresentationEligibility()
    {
        var input = CaptureManualCallInput(RequestOrigin.LocalUi, CallPolicyRevision);
        var call = CallPolicyRevision;
        var inputRevision = Interlocked.Read(ref promptInputRevision);
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var owner = settingsOwnershipRevision();
        return () => input.Eligible() && IsClipboardEligible(RequestOrigin.LocalUi, call)
            && inputRevision == Interlocked.Read(ref promptInputRevision)
            && binding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && owner == settingsOwnershipRevision();
    }
}
