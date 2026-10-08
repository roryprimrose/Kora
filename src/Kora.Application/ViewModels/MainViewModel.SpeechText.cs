using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SpeechTextConfigurationService? speechTextConfiguration;
    private readonly SpeechCaption speechCaption = new();
    private Guid captionResponseId = Guid.NewGuid();
    private string? speechCaptionText;
    private bool speechTextControlActive;
    private SpeechTextChoice? selectedSpeechTextChoice;
    private Func<HostActivity>? captionPresentationContext;

    public string? SpeechCaptionText => speechCaptionText;
    public bool IsSpeechCaptionVisible => speechCaptionText is not null && CanRevealPrivatePresentation
        && IsCallMutationHostEligible && IsSpeechResponseEnabled;
    public IReadOnlyList<SpeechTextChoice> SpeechTextChoices => speechTextConfiguration?.Choices ?? [];
    public SpeechTextChoice? SelectedSpeechTextChoice
    {
        get => selectedSpeechTextChoice;
        set => SetProperty(ref selectedSpeechTextChoice, value);
    }
    public bool CanChangeSpeechText => speechTextConfiguration is not null && IsCallMutationHostEligible
        && !speechTextControlActive && !IsResponseInteractionPending;
    public string SpeechTextConfigurationStatus => speechTextConfiguration is null
        ? "Speech-text control is unavailable; captions remain off."
        : SpeechTextState.Serialize(speechTextConfiguration.Get());
    public AsyncCommand RefreshSpeechTextCommand { get; }
    public AsyncCommand SaveSpeechTextCommand { get; }
    public AsyncCommand ResetSpeechTextCommand { get; }

    private Task StartCaptionedSpeechAsync(string exactText, Func<bool> playbackEligible)
    {
        RetireSpeechCaption();
        var captionState = speechTextConfiguration?.Get();
        if (captionState?.Effective is not SpeechTextMode.CurrentUtterance)
        {
            return textToSpeech.SpeakAsync(exactText, activeSpeechVoice!, SelectedOutputDevice!, CancellationToken.None);
        }
        var request = HostActivity.RequireCurrent().Request;
        captionPresentationContext = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation);
        var response = captionResponseId;
        var callRevision = CallPolicyRevision;
        var privacy = privacyObservation.Current;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var voiceGeneration = voiceRecognition.Generation;
        bool Eligible() => !disposed && playbackEligible() && response == captionResponseId
            && IsCallMutationHostEligible && CanRevealPrivatePresentation
            && callRevision == CallPolicyRevision
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
            && privacyObservation.Current.MicrophoneAccess == privacy.MicrophoneAccess
            && recovery == Interlocked.Read(ref voiceRecoveryRevision)
            && (request.Origin != RequestOrigin.ActivatedVoice || voiceRecognition.Generation == voiceGeneration)
            && speechTextConfiguration?.Get() is { Available: true, Effective: SpeechTextMode.CurrentUtterance } current
            && current.Revision == captionState.Revision;
        var id = speechCaption.Bind(response, request, exactText, Eligible);
        return textToSpeech.SpeakAsync(exactText, activeSpeechVoice!, SelectedOutputDevice!, id, CancellationToken.None);
    }

    private void RetireSpeechCaptionSource()
    {
        captionResponseId = Guid.NewGuid();
        RetireSpeechCaption();
    }

    private void RetireSpeechCaption()
    {
        speechCaption.Retire();
        captionPresentationContext = null;
        SetSpeechCaptionText(null);
    }

    public void ReportSpeechCaptionPresentationFailure(string exceptionType)
    {
        using var activity = captionPresentationContext?.Invoke();
        RetireSpeechCaption();
        if (activity is not null) { CaptionPresentationFailed(logger, exceptionType); }
        if (!disposed)
        {
            Transcript = "Speech-text presentation is unavailable until restart. The complete answer and required recovery remain visual.";
            forceVisualResponse = true;
            NotifyOutputPolicyChanged();
            if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
        }
        activity?.Complete(HostOperationOutcome.Failed);
    }

    private void SetSpeechCaptionText(string? text)
    {
        if (SetProperty(ref speechCaptionText, text, nameof(SpeechCaptionText)))
        {
            OnPropertyChanged(nameof(IsSpeechCaptionVisible));
        }
    }

    private void OnSpeechTextConfigurationChanged(object? sender, EventArgs args)
    {
        speechCaption.Retire();
        uiDispatcher.Post(SynchronizeSpeechTextConfiguration);
    }

    private void SynchronizeSpeechTextConfiguration()
    {
        RetireSpeechCaption();
        if (disposed) { return; }
        var captionState = speechTextConfiguration!.Get();
        SelectedSpeechTextChoice = SpeechTextChoices.FirstOrDefault(choice => choice.Mode == captionState.Effective);
        OnPropertyChanged(nameof(SpeechTextChoices));
        OnPropertyChanged(nameof(SpeechTextConfigurationStatus));
        OnPropertyChanged(nameof(CanChangeSpeechText));
    }

    private Task RunNativeSpeechTextAsync(AppearanceCommandOperation operation) =>
        ExecuteSpeechTextCommandAsync(new(operation), SecurityAuditInitiator.LocalUser,
            operation == AppearanceCommandOperation.Set ? SelectedSpeechTextChoice : null);

    internal async Task ExecuteSpeechTextCommandAsync(SpeechTextCommand command, SecurityAuditInitiator initiator,
        SpeechTextChoice? nativeChoice = null, CancellationToken cancellationToken = default)
    {
        if (!CanChangeSpeechText)
        {
            if (!disposed) { Transcript = "Speech-text configuration requires the owning unlocked host and no pending question or approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            Transcript = command.Error!;
            return;
        }
        var service = speechTextConfiguration!;
        var origin = OriginalOrigin(initiator);
        var eligible = CaptureAudioControlEligibility(origin);
        var callRevision = CallPolicyRevision;
        speechTextControlActive = true;
        OnPropertyChanged(nameof(CanChangeSpeechText));
        try
        {
            if (nativeChoice is null) { await service.RefreshAsync(origin, eligible, cancellationToken); }
            var outcome = "observed";
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var mode = command.Operation == AppearanceCommandOperation.Reset ? SpeechTextMode.Off : command.Value;
                var choice = nativeChoice ?? service.Choices.Single(item => item.Mode == mode);
                outcome = await service.SelectAsync(choice, callRevision, origin, initiator, communicationPolicy, eligible, cancellationToken)
                    ? "saved" : "denied";
            }
            if (!eligible()) { throw new InvalidOperationException("Speech-text presentation admission changed."); }
            var result = SpeechTextState.Serialize(service.Get(outcome));
            if (IsSpeaking) { Transcript = result; }
            else
            {
                PresentResponse(Kora.Core.AssistantState.Information, "Speech-text configuration.", result, refreshOutput: false);
                forceVisualResponse = true;
                NotifyOutputPolicyChanged();
                if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
        {
            service.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Inspecting or saving speech-text presentation preference");
            if (!disposed)
            {
                if (IsSpeaking || IsResponseInteractionPending) { Transcript = "Speech-text preference not confirmed. Captions remain off."; }
                else { ShowFailure("Speech-text preference not confirmed.", "Captions remain off. Inspect saved state and audit receipts before explicit repair."); }
            }
        }
        finally { speechTextControlActive = false; SynchronizeSpeechTextConfiguration(); }
    }
}
