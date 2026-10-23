using Kora.Application.Infrastructure;
using Kora.Application.Diagnostics;
using Kora.Application.Voice;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Tools.Speech;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel : ISpeechReadOncePlayback
{
    private readonly SpeechReadOnce speechReadOnce;
    private sealed record CurrentResponseSpeech(Guid Id, long Revision, AssistantState State,
        string Title, string Body, HostRequest? Request)
    {
        internal FinalizedModelAnswer? ModelAnswer { get; init; }
    }
    private CurrentResponseSpeech? ordinaryResponse;
    private long ordinaryResponseRevision;
    private CancellationTokenSource? responseReadCancellation;
    private Task responseReadQuiescence = Task.CompletedTask;
    private SecurityAuditEvent? responseReadAudit;
    private Func<bool> responseReadDecisionsEligible = static () => true;

    public void BindResponseReadDecisionGate(Func<bool> eligible)
    {
        RetireCurrentResponseSpeech();
        responseReadDecisionsEligible = eligible;
    }

    private sealed record ResponseReadInput(ResponseSpeechTarget? Target, SecurityAuditInitiator Initiator, Func<bool> Eligible);

    public AsyncCommand ReadCurrentResponseCommand { get; }
    public string ReadCurrentResponseStatus { get; private set; } = string.Empty;
    public string ResponseReadModelUnavailableReason => SpeechReadOnce.ModelLaneUnavailableReason;
    public bool IsCurrentResponseReadActive => responseReadCancellation is not null;
    public bool CanReadCurrentResponse => Volatile.Read(ref ordinaryResponse) is not null && IsSpeechResponseEnabled
        && !IsProtectedCall && !IsResponseInteractionPending && !IsGrantEditorVisible
        && responseReadDecisionsEligible() && !IsSpeechProviderOperationActive
        && !IsBusy && !IsSpeaking && responseReadCancellation is null;

    private ResponseReadInput CaptureResponseReadInput(SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var target = Volatile.Read(ref ordinaryResponse) is { } source
            ? new ResponseSpeechTarget(source.Id, source.Revision, CallPolicyRevision)
            : (ResponseSpeechTarget?)null;
        var origin = HostActivity.RequireCurrent().Request.Origin;
        if (origin == RequestOrigin.ActivatedVoice) { initiator = SecurityAuditInitiator.VoiceCommand; }
        var privacy = privacyObservation.Current;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var owner = auditImpactOwnerRevision();
        var speech = speechConfiguration.Get().Revision;
        var output = SelectedOutputDevice;
        var effectiveOutput = EffectiveOutputDevice;
        var mode = EffectiveResponseMode;
        var volume = audioPlaybackSettings.VolumeState?.Revision;
        var rate = audioPlaybackSettings.RateState?.Revision;
        var outputRevision = outputConfiguration?.Get(CallPolicyRevision).Revision;
        var responseModeRevision = responseModeConfiguration?.Get().Revision;
        var feedbackRevision = inCallFeedbackConfiguration?.Get().Revision;
        var voiceGeneration = voiceRecognition.Generation;
        return new(target, initiator, () => target is { } exact && IsCurrentResponseSpeechTarget(exact)
            && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && IsSpeechResponseEnabled && !IsProtectedCall && !IsResponseInteractionPending
            && responseReadDecisionsEligible() && !IsGrantEditorVisible && !IsBusy && !IsSpeechProviderOperationActive
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
            && privacyObservation.Current.MicrophoneAccess == privacy.MicrophoneAccess
            && privacyObservation.Current.SessionState == privacy.SessionState
            && recovery == Interlocked.Read(ref voiceRecoveryRevision)
            && owner == auditImpactOwnerRevision()
            && speech == speechConfiguration.Get().Revision
            && SelectedOutputDevice == output && EffectiveOutputDevice == effectiveOutput
            && EffectiveResponseMode == mode
            && volume == audioPlaybackSettings.VolumeState?.Revision
            && rate == audioPlaybackSettings.RateState?.Revision
            && outputRevision == outputConfiguration?.Get(CallPolicyRevision).Revision
            && responseModeRevision == responseModeConfiguration?.Get().Revision
            && feedbackRevision == inCallFeedbackConfiguration?.Get().Revision
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && voiceRecognition.Generation == voiceGeneration));
    }

    private bool IsCurrentResponseSpeechTarget(ResponseSpeechTarget target) =>
        Volatile.Read(ref ordinaryResponse)?.Id == target.ResponseId && target.ResponseId != Guid.Empty
        && target.Revision == Interlocked.Read(ref ordinaryResponseRevision)
        && target.PolicyRevision == CallPolicyRevision;

    private void RetireResponseReadPlayback()
    {
        if (responseReadCancellation is { } cancellation)
        {
            cancellation.Cancel();
            textToSpeech.InvalidateOutput();
        }
    }

    public void RetireCurrentResponseSpeech()
    {
        Interlocked.Exchange(ref ordinaryResponse, null);
        Interlocked.Increment(ref ordinaryResponseRevision);
        RetireResponseReadPlayback();
        uiDispatcher.Post(() => CurrentResponseChanged?.Invoke(this, EventArgs.Empty));
        uiDispatcher.Post(NotifyResponseReadChanged);
    }

    public event EventHandler? CurrentResponseChanged;

    private void BindCurrentResponseSpeech(bool eligible, FinalizedModelAnswer? modelAnswer = null)
    {
        if (eligible)
        {
            Volatile.Write(ref ordinaryResponse, new(Guid.NewGuid(), Interlocked.Read(ref ordinaryResponseRevision),
                State, ResponseTitle, ResponseBody, HostActivity.Current?.Request) { ModelAnswer = modelAnswer });
        }
        ReadCurrentResponseStatus = string.Empty;
        NotifyResponseReadChanged();
    }

    private void NotifyResponseReadChanged()
    {
        OnPropertyChanged(nameof(CanReadCurrentResponse));
        OnPropertyChanged(nameof(ReadCurrentResponseStatus));
        OnPropertyChanged(nameof(IsCurrentResponseReadActive));
        ReadCurrentResponseCommand.NotifyCanExecuteChanged();
    }

    private void ReportResponseRead(string status)
    {
        ReadCurrentResponseStatus = status;
        OnPropertyChanged(nameof(ReadCurrentResponseStatus));
        Transcript = status;
    }

    private void LogSpokenResponseFailure(Exception exception, string operation, bool oneShot)
    {
        if (oneShot) { ResponseReadFailed(logger, exception.GetType().Name); }
        else { ApplicationLog.Error(logger, exception, operation); }
    }

    private async Task ExecuteResponseReadCommandAsync(ResponseReadCommand command, string originalInput,
        SecurityAuditInitiator initiator, ResponseReadInput? capturedInput)
    {
        if (!command.IsValid)
        {
            ReportResponseRead("Response read command was not accepted. Use exactly read that aloud or read current response, with the optional current assistant name; no extra source, text or controls. Nothing was played, answered, approved or sent to a model.");
            return;
        }
        if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(originalInput, AssistantName))
        {
            ReportResponseRead("Assistant prefix routing is unavailable; no response playback was dispatched.");
            return;
        }
        var input = capturedInput ?? CaptureResponseReadInput(initiator);
        await Kora.Application.Hosting.HostRequestRunner.RunAsync(OriginalOrigin(initiator),
            () => ReadCurrentResponseAsync(input));
    }

    private async Task ReadCurrentResponseAsync(ResponseReadInput input)
    {
        if (input.Target is not { } target || !input.Eligible() || IsSpeaking || responseReadCancellation is not null)
        {
            ReportResponseRead("Read current response is unavailable: use an eligible current ordinary response, no pending decision, and the owning unlocked host. Normal speech mode, call/privacy, voice, volume and output admission still apply; inspect Speech and audio settings. No replay was queued.");
            return;
        }
        // Both native and exact command routes invoke the same action with no raw-text argument.
        if (State == AssistantState.Listening && Volatile.Read(ref ordinaryResponse) is { } source)
        {
            State = source.State;
        }
        await speechReadOnce.ExecuteAsync(target, input.Initiator, CancellationToken.None);
    }

    async Task ISpeechReadOncePlayback.ReadOnceAsync(ResponseSpeechTarget target, SecurityAuditInitiator initiator,
        CancellationToken cancellationToken)
    {
        var input = CaptureResponseReadInput(initiator);
        if (input.Target != target || !input.Eligible() || IsSpeaking || responseReadCancellation is not null)
        {
            ReportResponseRead("Read current response was refused: its exact source, policy or speech admission is no longer current.");
            return;
        }
        var request = HostActivity.RequireCurrent().Request;
        initiator = input.Initiator;
        if (request.Origin == RequestOrigin.LocalUi
            && initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand))
        {
            ReportResponseRead("Response playback requires original native, typed or activated-voice input.");
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMinutes(2));
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        responseReadCancellation = cancellation;
        responseReadQuiescence = released.Task;
        NotifyResponseReadChanged();
        SecurityAuditEvent? audit = null;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Presentation);
        try
        {
            audit = await StartAuditAsync(SecurityAuditCategory.ApplicationExecution, SpeechReadOnce.AuditActionId, initiator,
                target.ResponseId.ToString("D"));
            responseReadAudit = audit;
            bool Eligible() => input.Eligible() && !cancellation.IsCancellationRequested;
            var played = Eligible() && await TrySpeakCurrentResponseAsync(cancellationToken: cancellation.Token,
                additionalEligibility: Eligible);
            await WriteAuditAsync(audit.WithOutcome(played ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Cancelled,
                played ? "current-response-played" : "speech-unavailable-or-retired"), retiredResponseReadCancellation: !played);
            audit = null;
            activity.Complete(played ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            if (IsCurrentResponseSpeechTarget(target))
            {
                ReportResponseRead(played ? "Current response playback completed once. No replay is queued."
                    : "Current response was not played completely. Full visual output is retained; inspect speech length, privacy, voice, volume and output recovery. Make a fresh explicit request after recovery.");
            }
        }
        catch (OperationCanceledException)
        {
            if (audit is not null)
            {
                await WriteAuditAsync(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "cancelled"),
                    retiredResponseReadCancellation: true);
            }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or AggregateException)
        {
            ResponseReadFailed(logger, exception.GetType().Name);
            if (!disposed)
            {
                if (textToSpeech.IsSpeaking) { ClearActiveSpeechVoice(); }
                PreserveSpokenResponseFailure("Response playback or its audit/resource completion could not be confirmed. The full answer remains visual; no successful read is claimed. Inspect speech readiness before a fresh explicit request.");
                ReportResponseRead("Read current response is unavailable: playback/audit completion could not be confirmed.");
            }
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            responseReadAudit = null;
            responseReadCancellation = null;
            released.TrySetResult();
            NotifyResponseReadChanged();
        }
    }
}
