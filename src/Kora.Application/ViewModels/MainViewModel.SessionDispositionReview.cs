using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISessionDispositionReviewPresenter? dispositionReviewPresenter;
    private long dispositionReviewEpoch;

    public void BindSessionDispositionReview(ISessionDispositionReviewPresenter? presenter)
    {
        Interlocked.Increment(ref dispositionReviewEpoch);
        dispositionReviewPresenter = presenter;
    }

    internal async Task ExecuteSessionDispositionReviewAsync(SessionDispositionReviewCommand command,
        SecurityAuditInitiator initiator, bool activationPrefixed)
    {
        if (disposed) { return; }
        var epoch = Interlocked.Increment(ref dispositionReviewEpoch);
        if (command.Error is not null)
        {
            ShowInformation("Session disposition review not accepted.", command.Error);
            return;
        }
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            ShowInformation("Session disposition review blocked.", "Resolve the current question or approval explicitly first. No target or decision changed.");
            return;
        }
        var origin = initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        var presenter = dispositionReviewPresenter;
        var callRevision = CallPolicyRevision;
        var recoveryRevision = Volatile.Read(ref voiceRecoveryRevision);
        var originalName = AssistantName;
        bool Eligible() => !disposed && epoch == Interlocked.Read(ref dispositionReviewEpoch)
            && ReferenceEquals(presenter, dispositionReviewPresenter) && IsHostInputEligible && !IsProtectedCall
            && CallPolicyRevision == callRevision && pendingModelQuestion is null
            && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin == RequestOrigin.LocalUi || origin == RequestOrigin.ActivatedVoice
                && activationPrefixed && IsVoiceEnabled && HasVoiceConsent && CallObservation.AllowActivation
                && recoveryRevision == Volatile.Read(ref voiceRecoveryRevision)
                && string.Equals(originalName, AssistantName, StringComparison.Ordinal));
        if (!Eligible())
        {
            ShowInformation("Session disposition review denied.", "Use original typed input or the current activated assistant name with unchanged private host admission. Nothing was removed.");
            return;
        }
        if (presenter is null)
        {
            ShowInformation("Session disposition review unavailable.", "The private native Sessions review is not bound. No alternate window, session or confirmation is used.");
            return;
        }
        try
        {
            var displayed = false;
            await uiDispatcher.InvokeAsync(async () =>
            {
                if (!Eligible()) { throw new OperationCanceledException("Disposition review admission changed."); }
                displayed = await presenter.ReviewDispositionAsync(command, origin, Eligible, CancellationToken.None);
            });
            if (!Eligible()) { return; }
            ShowInformation(displayed ? "Session disposition preview opened." : "Session disposition preview not opened.",
                SessionDispositionReviewCommand.Boundary + " If unavailable, cancelled or stale, inspect the exact ID and revisions and request a fresh review.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.SessionDispositionReviewFailed(logger, exception.GetType().Name);
            if (!Eligible()) { return; }
            ShowFailure("Session disposition review unavailable.", "No removal was requested. Restore private native ownership, inspect the exact ID and revisions, and request a fresh review.");
        }
    }
}
