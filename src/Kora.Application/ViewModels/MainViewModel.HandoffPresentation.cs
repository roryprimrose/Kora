namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<bool> handoffReviewQuiescent = static () => true;

    public event EventHandler? HandoffReviewRequested;

    public void ReviewPendingHandoff() => HandoffReviewRequested?.Invoke(this, EventArgs.Empty);

    public void BindHandoffReviewQuiescence(Func<bool> observe) => handoffReviewQuiescent = observe;

    private bool IsHandoffReviewQuiescent => handoffReviewQuiescent();

    public Func<bool> CaptureHandoffPresentationEligibility()
    {
        var callRevision = CallPolicyRevision;
        var privacy = privacyObservation.Current;
        return () => IsCallMutationHostEligible
            && CallPolicyRevision == callRevision
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision;
    }
}
