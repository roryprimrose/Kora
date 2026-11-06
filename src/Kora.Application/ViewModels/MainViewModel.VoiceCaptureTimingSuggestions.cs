using Kora.Application.Configuration;
using Kora.Core.Conversation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Task timingSuggestionWork = Task.CompletedTask;

    public async Task<VoiceCaptureTimingSuggestionReview> ReviewVoiceCaptureTimingSuggestionAsync(
        ConversationMessage message, Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource,
        CancellationToken token)
    {
        var service = captureTiming ?? throw new InvalidOperationException("Capture timing is unavailable.");
        if (disposed || timingWriting || !timingSuggestionWork.IsCompleted)
        { throw new InvalidOperationException("Wait for the current capture timing operation before reviewing again."); }
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var owner = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var consent = HasVoiceConsent;
        var name = assistantNameConfiguration.Get().Revision;
        var microphone = SelectedMicrophone;
        var topology = MicrophoneTopologyRevision;
        bool Eligible() => !disposed && IsCallMutationHostEligible && !IsBusy && !IsResponseInteractionPending
            && privacyObservation.Current.TopologyRevision == privacy && CallPolicyRevision == call
            && settingsOwnershipRevision() == owner && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Interlocked.Read(ref voiceRecoveryRevision) == recovery && HasVoiceConsent == consent
            && assistantNameConfiguration.Get().Revision == name
            && SelectedMicrophone == microphone && MicrophoneTopologyRevision == topology && sourceCurrent();
        var work = VoiceCaptureTimingSuggestionReview.ReviewAsync(service, communicationPolicy,
            speechSuggestionBinding?.Time ?? TimeProvider.System, message, Eligible, validateSource, token);
        timingSuggestionWork = work;
        try { return await work.ConfigureAwait(false); }
        finally { timingSuggestionWork = Task.CompletedTask; }
    }

    public async Task<bool> ConfirmVoiceCaptureTimingSuggestionAsync(VoiceCaptureTimingSuggestionReview review,
        CancellationToken token)
    {
        if (disposed || timingWriting || !timingSuggestionWork.IsCompleted || captureTiming is null || !review.BelongsTo(captureTiming))
        { throw new InvalidOperationException("Capture timing confirmation is unavailable while closing or another save is pending."); }
        timingWriting = true;
        var work = review.ConfirmAsync(token);
        timingSuggestionWork = work;
        try { return await work.ConfigureAwait(false); }
        finally
        {
            timingSuggestionWork = Task.CompletedTask;
            timingWriting = false;
            NotifyVoiceTiming();
        }
    }
}
