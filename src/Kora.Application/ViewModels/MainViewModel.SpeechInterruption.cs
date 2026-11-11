using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record SpeechInterruptionInput(ForegroundConversationTarget? Session,
        RetainedAnswerSpeech? Playback, Func<bool> Eligible, long Generation = -1)
    {
        internal Func<bool> WakeCurrent { get; init; } = static () => false;
    }

    private enum SpeechInterruptionChannel { Typed, PushToTalk, Wake }

    private SpeechInterruptionInput? capturedSpeechInterruption;
    private bool speechInterruptionActive;

    private bool IsExactRetainedPlaybackWakeCapture =>
        voiceRecognition is Voice.HandsFreeVoiceRecognitionService { HasSpeechInterruptionCapture: true }
        && retainedAnswerSpeech is { } playback
        && ReferenceEquals(HostActivity.Current?.Request, playback.Request)
        && responseReadCancellation is { IsCancellationRequested: false };

    private SpeechInterruptionInput CaptureSpeechInterruptionInput()
    {
        var session = Volatile.Read(ref foregroundConversationTarget);
        var playback = retainedAnswerSpeech;
        var privacy = privacyObservation.Current;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var ownership = Interlocked.Read(ref voiceOwnershipBindingRevision);
        var call = CallPolicyRevision;
        var editor = Volatile.Read(ref customizationVoiceTarget);
        bool Eligible() => session is { IsCurrent: true } && playback is not null
            && playback.Request.SessionId == session.Entry.Authority.SessionId
            && ReferenceEquals(retainedAnswerSpeech, playback) && playback.Admitted()
            && editor is null && customizationVoiceTarget is null
            && !disposed && !lifecycleAdmissionClosed && IsHostInputEligible && voiceOwnershipEligible()
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
            && privacyObservation.Current.SessionState == privacy.SessionState
            && privacyObservation.Current.MicrophoneAccess == privacy.MicrophoneAccess
            && recovery == Interlocked.Read(ref voiceRecoveryRevision)
            && ownership == Interlocked.Read(ref voiceOwnershipBindingRevision) && call == CallPolicyRevision;
        return new(session, playback, Eligible);
    }

    private bool IsSpeechInterruptionCommand(string text) =>
        commandRouter.Match(text, AssistantName).Command?.Action == BuiltInAction.StopSpeaking;

    private bool IsSpeechInterruptionChannelCurrent(SpeechInterruptionInput input, SpeechInterruptionChannel channel) =>
        channel == SpeechInterruptionChannel.Typed || (channel == SpeechInterruptionChannel.Wake
            ? input.WakeCurrent() && voiceRecognition.IsCaptureQuiescent
            : input.Generation == voiceRecognition.Generation && HasVoiceConsent && IsVoiceEnabled
                && voiceRecognition.IsSpeechInterruptionAvailable);

    private async Task InterruptSessionSpeechAsync(SpeechInterruptionInput? input, SpeechInterruptionChannel channel)
    {
        if (input is null || !input.Eligible() || responseReadCancellation is null
            || !IsSpeechInterruptionChannelCurrent(input, channel))
        {
            ReportResponseRead("Speech interruption refused: the original session, playback or explicit command channel is unavailable or changed. No other speech or model work was stopped.");
            return;
        }
        if (channel == SpeechInterruptionChannel.PushToTalk)
        {
            await voiceRecognition.EndPushToTalkAsync();
            IsListening = voiceRecognition.IsListening;
            if (!voiceRecognition.IsCaptureQuiescent || input.Generation != voiceRecognition.Generation || !input.Eligible())
            {
                ReportResponseRead("Speech interruption unavailable: original command capture release or admission could not be confirmed. No other output was stopped.");
                return;
            }
        }
        var playback = input.Playback!;
        var original = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginRoot(new(original.RequestId, playback.Request.SessionId,
            original.TaskId, original.Origin, original.InvocationId), HostActivityLayer.Application,
            HostOperation.Request, [new(HostActivity.RequireCurrent().Activity!.Context)]);
        var audit = await StartAuditAsync(SecurityAuditCategory.ApplicationExecution, "speech.interrupt",
            channel == SpeechInterruptionChannel.Typed ? SecurityAuditInitiator.TypedCommand : SecurityAuditInitiator.VoiceCommand,
            playback.Target.ResponseId.ToString("D"));
        try
        {
            if (!input.Eligible() || !IsSpeechInterruptionChannelCurrent(input, channel))
            {
                await WriteAuditAsync(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "original-speech-target-retired"));
                activity.Complete(HostOperationOutcome.Cancelled);
                ReportResponseRead("Speech interruption refused: the original target retired before cancellation. Nothing was retargeted.");
                return;
            }
            speechInterruptionActive = true;
            NotifyDetailPresentationAvailability();
            playback.Interrupted = true;
            RetireResponseReadPlayback();
            RetireSpeechCaption();
            await textToSpeech.StopAsync();
#pragma warning disable VSTHRD003 // Join the captured one-shot; cancellation alone is not a release receipt.
            await responseReadPlaybackQuiescence;
#pragma warning restore VSTHRD003
            if (textToSpeech.IsSpeaking || !playback.InterruptionConfirmed)
            {
                throw new InvalidOperationException("Speech release was not confirmed.");
            }
            await WriteAuditAsync(audit.WithOutcome(SecurityAuditOutcome.Succeeded, "exact-session-speech-interrupted"));
            ReportResponseRead("Exact session speech interrupted. Playback and command capture are released; no model work was cancelled or replay queued.");
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SpeechInterruptionFailed(logger, exception.GetType().Name);
            if (textToSpeech.IsSpeaking)
            {
                ClearActiveSpeechVoice();
                IsSpeaking = true;
            }
            await WriteAuditAsync(audit.WithOutcome(SecurityAuditOutcome.Failed, "speech-release-unconfirmed"));
            ReportResponseRead("Speech interruption is unconfirmed. Owned playback remains in recovery; use native Stop and inspect speech readiness. No successful interruption is claimed.");
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            speechInterruptionActive = false;
            NotifyDetailPresentationAvailability();
            NotifyResponseReadChanged();
        }

    }

    private Task<Voice.WakeSpeechInterruptionCapture?> StartWakeSpeechInterruptionAsync(
        Voice.HandsFreeVoiceRecognitionService owner, CancellationToken token)
    {
        var input = CaptureSpeechInterruptionInput();
        if (!input.Eligible())
        { return Task.FromResult<Voice.WakeSpeechInterruptionCapture?>(null); }
        return owner.StartSpeechInterruptionAsync(input.Eligible, (transcript, current) =>
            uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(RequestOrigin.ActivatedVoice, async () =>
            {
                if (!current() || !input.Eligible() || !IsSpeechInterruptionCommand(transcript.Transcript)) { return; }
                await InterruptSessionSpeechAsync(input with { WakeCurrent = current }, SpeechInterruptionChannel.Wake);
            })), token);
    }
}
