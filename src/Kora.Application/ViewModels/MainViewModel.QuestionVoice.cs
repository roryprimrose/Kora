using System.ComponentModel;
using Kora.Application.Interaction;
using Kora.Application.Voice;
using Kora.Core.Communication;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private HostQuestionVoiceReplyService? questionVoiceReplies;
    private bool questionSpeech;

    public HostQuestionVoiceReplyService? BindQuestionVoiceReplies(HostQuestionService questions,
        TimeProvider time, ILogger<HostQuestionVoiceReplyService> replyLogger)
    {
        if (questionVoiceReplies is not null)
        {
            throw new InvalidOperationException("The application already has a native question voice owner.");
        }
        if (voiceRecognition is not IQuestionVoiceRecognitionService capture) { return null; }
        questionVoiceReplies = new(questions, capture, time, uiDispatcher, ObserveQuestionVoiceAdmission,
            SpeakExactQuestionAsync, replyLogger, TryStopQuestionListeningAsync, GetListeningStopPhrases);
        PropertyChanged += OnQuestionVoiceReadinessChanged;
        return questionVoiceReplies;
    }

    private IEnumerable<string> GetListeningStopPhrases() => commandCatalog.GetCommands(AssistantName)
        .Single(command => command.Action == BuiltInAction.DisableListening).AllPhrases
        .SelectMany(phrase => new[] { phrase, $"{AssistantName} {phrase}" });

    private async Task<bool> TryStopQuestionListeningAsync(string transcript)
    {
        if (!IsListeningStopCommand(transcript)) { return false; }
        await Kora.Application.Hosting.HostRequestRunner.RunAsync(RequestOrigin.ActivatedVoice,
            () => DisableListeningAsync(SecurityAuditInitiator.VoiceCommand));
        return true;
    }

    private QuestionVoiceAdmission? ObserveQuestionVoiceAdmission()
    {
        var privacy = privacyObservation.Current;
        return IsVoiceEnabled && IsHostInputEligible && voiceOwnershipEligible()
            && microphoneCatalogCurrent && catalogPrivacyRevision == privacy.TopologyRevision
            && IsVoiceReadinessEligible(privacy)
            && communicationPolicy.Current.EffectiveState is CallState.Unavailable or CallState.Clear or CallState.Active or CallState.Suspected
            && !IsBusy && !IsResponseInteractionPending && (!textToSpeech.IsSpeaking && !IsSpeaking || questionSpeech)
                ? new(EffectiveMicrophone!, Interlocked.Read(ref voiceRecoveryRevision), privacy.TopologyRevision, CallPolicyRevision)
                : null;
    }

    private void OnQuestionVoiceReadinessChanged(object? sender, PropertyChangedEventArgs args) =>
        questionVoiceReplies!.Revalidate();

    public void RetireQuestionVoiceReply() =>
        questionVoiceReplies?.Retire("Voice admission changed. Microphone closed; native question remains answerable.");

    private async Task SpeakExactQuestionAsync(string text, Func<bool> eligible, CancellationToken token)
    {
        var voice = activeSpeechVoice;
        var output = SelectedOutputDevice;
        if (voice is null || output is null || !eligible() || !IsSpeechResponseEnabled || textToSpeech.IsSpeaking || IsSpeaking)
        {
            throw new InvalidOperationException("Exact question playback is unavailable. Capture was not opened.");
        }
        questionSpeech = true;
        IsSpeaking = true;
        var started = false;
        try
        {
            await communicationPolicy.StartSpeech(() =>
            {
                token.ThrowIfCancellationRequested();
                started = true;
                return textToSpeech.SpeakAsync(text, voice, output, token);
            }, () => eligible() && !token.IsCancellationRequested);
            if (!started || !eligible() || textToSpeech.IsSpeaking)
            {
                throw new InvalidOperationException("Exact question playback did not complete under the original admission.");
            }
        }
        finally
        {
            questionSpeech = false;
            IsSpeaking = false;
        }
    }
}
