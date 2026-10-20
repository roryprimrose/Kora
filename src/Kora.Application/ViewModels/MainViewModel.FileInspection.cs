namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public Func<bool> CaptureLocalFileInspectionEligibility()
    {
        var current = CapturePromptSelection();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var owner = settingsOwnershipRevision();
        return () => current() && binding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && owner == settingsOwnershipRevision();
    }
}
