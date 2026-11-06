using Kora.Application.Communication;
using Kora.Application.Infrastructure;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private CallVoiceActivationReview? callVoiceActivationReview;
    private long callVoiceActivationNativeRevision;
    private int callVoiceActivationNativeAvailable;
    private long callVoiceActivationReviewRevision;
    private string? callVoiceActivationReviewStatus;
    private AsyncCommand? reviewCallVoiceActivation;
    private AsyncCommand? confirmCallVoiceActivation;
    private AsyncCommand? cancelCallVoiceActivation;

    public string CallVoiceActivationReviewStatus => callVoiceActivationReviewStatus
        ?? "To restore call-time activation, start a new native review. Saving never enables listening or spoken approvals.";

    public AsyncCommand ReviewCallVoiceActivationCommand => reviewCallVoiceActivation ??= new(async () =>
    {
        CancelCallVoiceActivationReview();
        RequireNativeCallVoiceActivation();
        var call = communicationPolicy.Current;
        var privacy = privacyObservation.Current.TopologyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var native = Interlocked.Read(ref callVoiceActivationNativeRevision);
        var lifetime = Interlocked.Read(ref callVoiceActivationReviewRevision);
        bool Eligible() => IsCallMutationHostEligible
            && native == Interlocked.Read(ref callVoiceActivationNativeRevision)
            && Volatile.Read(ref callVoiceActivationNativeAvailable) == 1
            && privacy == privacyObservation.Current.TopologyRevision
            && ownership == settingsOwnershipRevision()
            && binding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && input == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && recovery == Interlocked.Read(ref voiceRecoveryRevision)
            && lifetime == Interlocked.Read(ref callVoiceActivationReviewRevision);
        callVoiceActivationReviewStatus = "Call-time activation review not confirmed. No confirmation is available until the saved preference has been checked.";
        OnPropertyChanged(nameof(CallVoiceActivationReviewStatus));
        var saved = await Task.Run(callAwarePreferences.Load);
        callVoiceActivationReview = communicationPolicy.ReviewVoiceActivation(OriginalOrigin(), call.Revision, saved, Eligible);
        callVoiceActivationReviewStatus = $"Review device-local voice activation during calls: Off -> On. Current call observation: {call.EffectiveState}. "
            + "This permits otherwise eligible activated commands and question replies only. It does not permit speech, spoken approval, "
            + "listening consent or automatic microphone recovery. Manual and recovery holds remain. Confirm separately or cancel.";
        OnPropertyChanged(nameof(CallVoiceActivationReviewStatus));
    }, HandleCommandException);

    public AsyncCommand ConfirmCallVoiceActivationCommand => confirmCallVoiceActivation ??= new(async () =>
    {
        var review = Interlocked.Exchange(ref callVoiceActivationReview, null);
        callVoiceActivationReviewStatus = "Call-time activation save not confirmed. Start a fresh native review; no automatic retry or listening recovery.";
        try
        {
            RequireNativeCallVoiceActivation();
            if (review is null)
            { throw new InvalidOperationException("Review the exact call-time activation change first."); }
            var outcome = await SetCallSettingsAsync(
                review.Observation.Settings with { AllowVoiceActivationDuringCalls = true }, activationReview: review);
            if (outcome == CallMutationOutcome.Applied)
            {
                callVoiceActivationReviewStatus = "Saved on this device: call-time voice activation On. "
                    + $"Current call observation: {communicationPolicy.Current.EffectiveState}. "
                    + "Eligibility still requires all listening and privacy gates. Capture was not opened; use separate Enable listening when permitted. "
                    + "Speech, spoken approvals, consent and manual/recovery holds are unchanged.";
            }
            else
            {
                callVoiceActivationReviewStatus = "Call-time activation save not confirmed. Inspect saved settings and start a fresh native review; "
                    + "no rollback, automatic retry or listening recovery is claimed.";
            }
        }
        finally { OnPropertyChanged(nameof(CallVoiceActivationReviewStatus)); }
    }, HandleCommandException);

    public AsyncCommand CancelCallVoiceActivationCommand => cancelCallVoiceActivation ??= new(() =>
    {
        CancelCallVoiceActivationReview();
        return Task.CompletedTask;
    }, HandleCommandException);

    public void BindCallVoiceActivationNativeLifetime(bool available)
    {
        Volatile.Write(ref callVoiceActivationNativeAvailable, available ? 1 : 0);
        Interlocked.Increment(ref callVoiceActivationNativeRevision);
        CancelCallVoiceActivationReview();
    }

    public void CancelCallVoiceActivationReview()
    {
        Interlocked.Increment(ref callVoiceActivationReviewRevision);
        Interlocked.Exchange(ref callVoiceActivationReview, null);
        callVoiceActivationReviewStatus = "No call-time activation review is pending. Cancellation does not save a preference or roll back an in-flight save.";
        OnPropertyChanged(nameof(CallVoiceActivationReviewStatus));
    }

    private void RequireNativeCallVoiceActivation()
    {
        if (!IsCallMutationHostEligible
            || Volatile.Read(ref callVoiceActivationNativeAvailable) != 1
            || OriginalOrigin() != RequestOrigin.LocalUi
            || HostActivity.HasScope && HostActivity.Current is null
            || HostActivity.Current?.Request.InvocationId is not null)
        {
            throw new InvalidOperationException("Fresh original native Calls settings and current private ownership are required; voice/model origin cannot be relabeled.");
        }
    }
}
