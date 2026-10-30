using Kora.Application.Interaction;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ConversationCompletionVoiceNotices? completionNotices;
    private long explicitVoiceActivationRevision = -1;
    private long completionPromptRevision;
    private bool completionNoticeSpeaking;

    public string CompletionNoticeDisclosure => ConversationCompletionVoiceNotices.Disclosure;
    public string CompletionNoticeStatus => "Task completion voice notices for this run: "
        + (completionNotices?.Enabled == true ? "On" : "Off") + ". "
        + (CanEnableCompletionNotices ? "Explicit current-run voice readiness is active."
            : "Unavailable: explicitly Enable listening this run and keep an active conversation foreground; restored readiness is not sufficient.")
        + " Last speech outcome: " + (completionNotices?.Outcome.ToString() ?? "NotRequested") + ".";
    public bool CanEnableCompletionNotices => completionNotices is not null && ExplicitCompletionVoiceCurrent()
        && !IsProtectedCall && !IsSpeaking && !textToSpeech.IsSpeaking && !IsResponseInteractionPending
        && pendingPowerAction is null && foregroundRequestReview is null
        && IsSpeechOutputAvailable && EffectiveResponseMode != Kora.Core.Voice.ResponseOutputMode.VisualOnly
        && foregroundConversationTarget is { IsCurrent: true, Entry.Authority.IsActive: true };

    private bool ExplicitCompletionVoiceCurrent() => !disposed && !lifecycleAdmissionClosed && IsHostInputEligible
        && voiceOwnershipEligible() && HasVoiceConsent && IsVoiceEnabled
        && Interlocked.Read(ref explicitVoiceActivationRevision) >= 0
        && Interlocked.Read(ref explicitVoiceActivationRevision) == Interlocked.Read(ref voiceRecoveryRevision);

    public Task ChangeCompletionNoticesNativeAsync(HostId<SessionIdentity> session, HostRevision generation,
        bool enabled, Func<bool> nativeCurrent, CancellationToken token)
    {
        var notices = completionNotices ?? throw new InvalidOperationException("Completion voice notices are unavailable.");
        var foreground = Volatile.Read(ref foregroundConversationTarget);
        if (enabled && (!CanEnableCompletionNotices || foreground!.Entry.Authority.SessionId != session
            || foreground.Entry.Authority.Generation != generation))
        { throw new InvalidOperationException("The exact explicitly active current-run voice conversation is unavailable."); }
        var recovery = Interlocked.Read(ref explicitVoiceActivationRevision);
        var privacy = privacyObservation.Current;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref voiceOwnershipBindingRevision);
        var call = CallPolicyRevision;
        bool VoiceCurrent() => ExplicitCompletionVoiceCurrent()
            && recovery == Interlocked.Read(ref explicitVoiceActivationRevision)
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
            && settingsOwnershipRevision() == ownership
            && CallPolicyRevision == call
            && Interlocked.Read(ref voiceOwnershipBindingRevision) == binding;
        return notices.ChangeNativeAsync(session, generation, enabled, notices.Revision, VoiceCurrent, nativeCurrent, token);
    }

    public void RetireCompletionNotices() => completionNotices?.Retire();

    public async Task DeliverCompletionNoticesAsync(LocalEventSnapshot snapshot, Func<bool> nativeCurrent,
        CancellationToken token)
    {
        if (completionNotices is not { } notices) { return; }
        var foreground = Volatile.Read(ref foregroundConversationTarget);
        var privacy = privacyObservation.Current;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var call = CallPolicyRevision;
        var response = Interlocked.Read(ref webResponseRevision);
        var prompt = Interlocked.Read(ref completionPromptRevision);
        var input = voiceRecognition.Generation;
        var command = CommandText;
        var voice = activeSpeechVoice;
        var output = SelectedOutputDevice;
        var speechSettings = speechConfiguration.Get();
        var fixedMeasure = Kora.Core.Voice.SpokenSummaryMeasure.Count(ConversationCompletionVoiceNotices.Statement);
        bool Gap() => !disposed && ExplicitCompletionVoiceCurrent() && nativeCurrent()
            && foreground is { IsCurrent: true } && ReferenceEquals(foreground, foregroundConversationTarget)
            && snapshot.Events.Any(view => view.Event.SessionId == foreground.Entry.Authority.SessionId
                && view.Event.Generation == foreground.Entry.Authority.Generation.Value)
            && !IsProtectedCall && call == CallPolicyRevision && !IsResponseInteractionPending
            && pendingPowerAction is null && foregroundRequestReview is null
            && !IsListening && !voiceRecognition.IsListening && voiceRecognition.IsCaptureQuiescent
            && promptInputVoice?.IsActive != true && composerDictation?.IsActive != true && questionVoiceReplies?.IsActive != true
            && recovery == Interlocked.Read(ref voiceRecoveryRevision) && input == voiceRecognition.Generation
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
            && response == Interlocked.Read(ref webResponseRevision) && prompt == Interlocked.Read(ref completionPromptRevision)
            && string.Equals(command, CommandText, StringComparison.Ordinal)
            && activeSpeechVoice == voice && SelectedOutputDevice == output && IsSpeechOutputAvailable
            && speechConfiguration.Get().Revision == speechSettings.Revision
            && speechSettings.SummaryLimits is { } limits && fixedMeasure.Fits(limits)
            && EffectiveResponseMode != Kora.Core.Voice.ResponseOutputMode.VisualOnly
            && (!IsSpeaking || completionNoticeSpeaking) && (!textToSpeech.IsSpeaking || completionNoticeSpeaking);
        var reserve = notices.Enabled && snapshot.Events.Any(view => view.Reason == LocalEventReason.Eligible
            && view.Event.Source == LocalEventSource.ConversationTask && view.Event.Type == LocalEventType.Completed)
            && !IsSpeaking && !textToSpeech.IsSpeaking && Gap();
        if (reserve) { completionNoticeSpeaking = true; IsSpeaking = true; }
        try { await notices.DeliverAsync(snapshot, () => reserve && Gap(), voice, output, token); }
        finally
        {
            if (reserve) { completionNoticeSpeaking = false; IsSpeaking = textToSpeech.IsSpeaking; }
            OnPropertyChanged(nameof(CompletionNoticeStatus));
            OnPropertyChanged(nameof(CanEnableCompletionNotices));
        }
    }

    private void CompletionNoticeHostChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        completionNotices!.Revalidate();
}