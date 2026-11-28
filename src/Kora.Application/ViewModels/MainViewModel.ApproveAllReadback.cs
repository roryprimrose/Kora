using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public bool IsApproveAllReadback { get; private set; }
    public bool CanConfirmForegroundRequest => HasForegroundRequestReview && !IsApproveAllReadback;
    public string ForegroundRequestHeading => IsApproveAllReadback
        ? "Approve all - sending after readback. Cancel to stop."
        : "Review original request - nothing sent yet";

    private void SetApproveAllReadback(bool value)
    {
        IsApproveAllReadback = value;
        OnPropertyChanged(nameof(IsApproveAllReadback));
        OnPropertyChanged(nameof(CanConfirmForegroundRequest));
        OnPropertyChanged(nameof(ForegroundRequestHeading));
    }

    public SessionRoutingVoiceReply? CreateApproveAllReadbackReply(SessionRoutingReview review, Func<bool> exact)
    {
        if (review.Origin != RequestOrigin.ActivatedVoice || sessionRoutingVoiceRequest is not { } request) { return null; }
        if (review.Preselection is not { Basis: SessionRoutingReview.PreselectionBasis.Foreground } destination
            || !string.Equals(request.Body, review.Body, StringComparison.Ordinal) || sessionRoutingVoiceReply is not null)
        { throw new InvalidOperationException("Automatic readback requires the exact unowned original foreground voice request."); }
        var reply = SessionRequestVoiceReply.Create(request, destination.Destination.Name, exact,
            () => ObserveSessionRoutingVoiceAdmission(request), SpeakSendReviewAsync, logger, submitAfterReadback: true);
        sessionRoutingVoiceReply = reply;
        return reply;
    }
}
