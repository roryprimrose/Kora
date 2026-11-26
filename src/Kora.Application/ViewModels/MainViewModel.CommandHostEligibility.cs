namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<bool> CaptureCommandHostEligibility()
    {
        var admitted = IsCallMutationHostEligible;
        var privacy = privacyObservation.Current.TopologyRevision;
        var owner = auditImpactOwnerRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        return () => admitted && IsCallMutationHostEligible
            && privacy == privacyObservation.Current.TopologyRevision
            && owner == auditImpactOwnerRevision()
            && binding == Interlocked.Read(ref settingsOwnershipBindingRevision);
    }
}
