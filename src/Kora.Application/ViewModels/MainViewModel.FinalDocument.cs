using Kora.Application.Network.ExternalBrowser;
using Kora.Application.Presentation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public void CancelExternalBrowserReview(Guid reviewId) => externalBrowser?.Retire(reviewId);

    public ExternalBrowserReview PrepareExternalBrowser(FinalDocumentNavigationRequest request, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        var privacy = privacyObservation.Current.TopologyRevision;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var call = CallPolicyRevision;
        bool Current() => !disposed && CanRevealPrivatePresentation && IsCallMutationHostEligible
            && !IsResponseInteractionPending && !IsBusy && !IsWebPageRetrievalActive && !IsWebSummaryActive
            && privacyObservation.Current.TopologyRevision == privacy
            && Interlocked.Read(ref voiceRecoveryRevision) == recovery && CallPolicyRevision == call && current();
        return (externalBrowser ?? throw new InvalidOperationException("The native external-browser workflow is unavailable."))
            .Prepare(request, Current);
    }
}
