using Kora.Application.Diagnostics;
using Kora.Core;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Application.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? captureOpenCancellation;
    private int pushToTalkHeld;
    private long acceptedTranscriptGeneration = -1;
    private Task privacyClosureTask = Task.CompletedTask;
    private long observedTopologyRevision = -1;
    private bool disposed;
    private readonly BoundedMicrophoneCatalog microphoneCatalog;
    private CancellationTokenSource? microphoneRefreshCancellation;
    private long catalogPrivacyRevision;
    private bool microphoneCatalogCurrent = true;
    private bool isRefreshingMicrophones;
    private Task microphoneRefreshTask = Task.CompletedTask;
    internal Task MicrophoneRefreshTask => microphoneRefreshTask;

    public bool IsRefreshingMicrophones => isRefreshingMicrophones;
    public bool IsMicrophoneCatalogCurrent => microphoneCatalogCurrent;
    public bool IsSystemMicrophoneAvailable => systemDefaultMicrophone is not null;
    public bool CanUseTrayMicrophoneRecovery => IsCallMutationHostEligible;

    public string TrayInputStatus => !IsCallMutationHostEligible
        ? "Input unavailable - ownership or Windows privacy recovery required"
        : !microphoneCatalogCurrent
            ? "Input unavailable - refresh microphones"
        : !HasVoiceConsent
            ? "Microphone closed - voice consent not granted"
        : MicrophoneAccessStatus.State != MicrophoneAccessState.Allowed
            ? "Microphone closed - Windows access denied or unknown"
        : EffectiveMicrophone is null
            ? "Microphone closed - selected microphone unavailable"
        : IsListening && voiceRecognition.IsListening
            ? "Push-to-talk capture active"
        : IsVoiceEnabled
            ? "Push-to-talk ready - microphone closed - wake unavailable"
            : "Microphone closed - listening disabled";

    public bool IsPrivacyPresentationHeld => Volatile.Read(ref privacyPresentationHeld) != 0;

    private bool IsHostInputEligible => !lifecycleAdmissionClosed && !IsPrivacyPresentationHeld
        && sessionController.IsCurrentSessionUnlocked()
        && privacyObservation.Current.SessionState == WindowsSessionState.Unlocked;

    public bool CanRevealPrivatePresentation => IsHostInputEligible;

    private void OnWindowsPrivacyChanged(object? sender, WindowsPrivacyChangedEventArgs eventArgs)
    {
        if (disposed) { return; }
        var snapshot = eventArgs.Current;
        if (snapshot.SessionState != WindowsSessionState.Unlocked)
        {
            CloseForObservedPrivacyEvent("Windows session is " + snapshot.SessionState, hidePresentation: true);
        }
        else if (snapshot.MicrophoneAccess != MicrophoneAccessState.Allowed
            || SelectedMicrophone is { } microphone && IsVoiceEnabled && !snapshot.CanCaptureFrom(microphone))
        {
            CloseForObservedPrivacyEvent("Microphone permission or selected endpoint is unavailable", hidePresentation: false);
        }
        if (SelectedOutputDevice?.IsSystemDefault == true && snapshot.DefaultSpeakerId is null)
        {
            InvalidateUnavailableOutput();
        }
        if (Interlocked.Exchange(ref observedTopologyRevision, snapshot.TopologyRevision) != snapshot.TopologyRevision)
        {
            uiDispatcher.Post(() =>
            {
                if (disposed)
                {
                    return;
                }
                RefreshOutputEndpoints();
                _ = RefreshMicrophonesAsync();
            });
        }
    }

    private void InvalidateUnavailableOutput()
    {
        if (textToSpeech.IsSpeaking)
        {
            textToSpeech.InvalidateOutput();
            privacyClosureTask = StopOutputAfterTopologyChangeAsync();
        }
    }

    private async Task StopOutputAfterTopologyChangeAsync()
    {
        try
        {
            await textToSpeech.StopAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            ApplicationLog.Error(logger, exception, "Stopping output after a Windows route change");
            uiDispatcher.Post(() => ShowFailure("Audio output route changed.", exception.Message));
        }
    }

    private bool RefreshOutputEndpoints()
    {
        try
        {
            var previousOutput = selectedOutputDevice;
            var devices = textToSpeech.GetOutputDevices();
            systemDefaultOutputDevice = textToSpeech.GetDefaultOutputDevice();
            var previous = SelectedOutputDevice;
            suppressAudioDevicePreferenceSave = true;
            try
            {
                OutputDevices.Clear();
                OutputDevices.Add(SystemAudioDevices.Output);
                foreach (var device in devices)
                {
                    OutputDevices.Add(device);
                }
                if (previousOutput is not null)
                {
                    SelectedOutputDevice = OutputDevices.FirstOrDefault(device =>
                        string.Equals(device.Id, previousOutput.Id, StringComparison.Ordinal)) ?? previousOutput;
                }
            }
            finally
            {
                suppressAudioDevicePreferenceSave = false;
            }
            UpdateOutputDeviceAvailability(selectedDeviceUnavailable: SelectedOutputDevice is not null
                && !OutputDevices.Contains(SelectedOutputDevice));
            PreviewVoiceCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSpeechOutputAvailable));
            NotifyOutputPolicyChanged();
            if (EffectiveOutputDevice is not { IsMuted: false })
            {
                InvalidateUnavailableOutput();
            }
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            InvalidateUnavailableOutput();
            ApplicationLog.Error(logger, exception, "Refreshing Windows output endpoint availability");
            ShowFailure("Audio output is unavailable.", exception.Message);
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        lifecycleAdmissionClosed = true;
        // Refresh cancellation can release an awaiting caller before Dispose returns.
        HoldVoiceInput("Microphone closed · host disposed");
        microphoneRefreshCancellation?.Cancel();
        clipboardPreview.Changed -= OnClipboardPreviewChanged;
        clipboardPreview.Dispose();
        privacyObservation.Changed -= OnWindowsPrivacyChanged;
        voiceRecognition.TranscriptRecognized -= OnTranscriptRecognized;
        voiceRecognition.RecognitionFailed -= OnRecognitionFailed;
        voiceRecognition.CaptureStateChanged -= OnCaptureStateChanged;
        voiceRecognition.RecognitionCompleted -= OnRecognitionCompleted;
        communicationPolicy.Changed -= OnCommunicationPolicyChanged;
        communicationPolicy.Dispose();
        textToSpeech.InvalidateOutput();
        appearanceConfiguration.Changed -= OnAppearanceChanged;
        speechConfiguration.Changed -= OnSpeechConfigurationChanged;
    }

    public event EventHandler? PrivacyClosureRequested;

    public event EventHandler? VoiceRecoveryRequested;

    private void OnCaptureStateChanged(object? sender, VoiceCaptureStateChangedEventArgs eventArgs)
    {
        if (eventArgs.IsListening)
        {
            return;
        }
        uiDispatcher.Post(() =>
        {
            if (eventArgs.Generation != Interlocked.Read(ref acceptedTranscriptGeneration)
                || eventArgs.Generation != voiceRecognition.CaptureGeneration || voiceRecognition.IsListening)
            {
                return;
            }
            Interlocked.Exchange(ref pushToTalkHeld, 0);
            IsListening = false;
            NotifyVoiceEnablementChanged();
        });
    }

    private void OnRecognitionCompleted(object? sender, VoiceRecognitionCompletedEventArgs eventArgs)
    {
        uiDispatcher.Post(() =>
        {
            if (eventArgs.Generation != Interlocked.Read(ref acceptedTranscriptGeneration)
                || voiceRecognition.IsListening || !IsHostInputEligible)
            {
                return;
            }
            switch (eventArgs.Reason)
            {
                case VoiceRecognitionCompletionReason.EmptySpeechTimeout:
                case VoiceRecognitionCompletionReason.NoSpeechRecognized:
                    Interlocked.CompareExchange(ref acceptedTranscriptGeneration, -1, eventArgs.Generation);
                    Interlocked.Exchange(ref pushToTalkHeld, 0);
                    IsListening = false;
                    ShowInformation("No command was heard.", "The microphone is closed. Use a new push-to-talk activation to try again.");
                    break;
                case VoiceRecognitionCompletionReason.MaximumDuration:
                    IsListening = false;
                    ShowInformation("Command capture reached its duration limit.",
                        "The microphone is closed; any final result still belongs only to this activation.");
                    break;
            }
        });
    }

    internal Task PrivacyClosureTask => privacyClosureTask;

    internal void CloseForObservedPrivacyEvent(string reason, bool hidePresentation)
    {
        if (hidePresentation)
        {
            Interlocked.Exchange(ref privacyPresentationHeld, 1);
            ClearClipboardPreview();
        }
        HoldVoiceInput("Microphone closed · " + reason + "; use Enable listening");
        textToSpeech.InvalidateOutput();
        privacyClosureTask = CompletePrivacyClosureAsync(reason, hidePresentation);
    }

    private async Task CompletePrivacyClosureAsync(string reason, bool hidePresentation)
    {
        try
        {
            uiDispatcher.Post(() =>
            {
                IsListening = false;
                IsSpeaking = false;
                activeSpokenText = null;
                OnPropertyChanged(nameof(IsPrivacyPresentationHeld));
                if (hidePresentation)
                {
                    if (IsModelActionApprovalPending)
                    {
                        ClearPendingModelAction("windows-privacy-transition");
                    }
                    if (IsGrantChangePending)
                    {
                        ClearPendingGrantChange("windows-privacy-transition");
                    }
                    if (IsModelQuestionPending)
                    {
                        ClearPendingModelQuestion();
                    }
                    Transcript = "No command heard yet.";
                    ResponseTitle = "Windows privacy recovery";
                    ResponseBody = "Return to an unlocked interactive session and use native controls. Listening remains disabled for this run.";
                    PrivacyClosureRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ShowInformation("Voice recovery is required.", reason + ". Refresh microphones and explicitly enable listening.");
                }
                NotifyVoiceEnablementChanged();
            });
            // Capture release never waits for speech synthesis or the UI dispatcher.
            var captureClosure = voiceRecognition.StopAsync();
            var outputClosure = textToSpeech.StopAsync();
            await Task.WhenAll(captureClosure, outputClosure);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Closing capture and output after a Windows privacy event");
            uiDispatcher.Post(() => ShowFailure("Audio privacy closure needs attention.", exception.Message));
        }
    }

    public void RequestVoiceRecovery()
    {
        if (!sessionController.IsCurrentSessionUnlocked() || lifecycleAdmissionClosed)
        {
            ApplicationLog.Information(logger, "Denied voice recovery display outside an eligible Windows session");
            return;
        }
        ShowSettings();
        VoiceRecoveryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyVoiceEnablementChanged()
    {
        OnPropertyChanged(nameof(TrayInputStatus));
        OnPropertyChanged(nameof(IsVoiceEnabled));
        OnPropertyChanged(nameof(ListeningButtonText));
        OnPropertyChanged(nameof(ListeningStatus));
        ToggleListeningCommand.NotifyCanExecuteChanged();
        BeginPushToTalkCommand.NotifyCanExecuteChanged();
    }

    private void HoldVoiceInput(string reason)
    {
        Interlocked.Increment(ref voiceRecoveryRevision);
        Interlocked.Increment(ref microphoneTopologyRevision);
        Interlocked.Exchange(ref voiceEnabled, 0);
        Interlocked.Exchange(ref acceptedTranscriptGeneration, -1);
        Interlocked.Exchange(ref pushToTalkHeld, 0);
        voiceRecognition.InvalidateCapture();
        captureOpenCancellation?.Cancel();
        uiDispatcher.Post(() =>
        {
            IsListening = false;
            SetListeningPauseReason(reason);
            NotifyVoiceEnablementChanged();
            OnPropertyChanged(nameof(MicrophoneTopologyRevision));
        });
    }

    public async Task SetVoiceConsentAsync(bool consent)
    {
        if (!AdmitVoiceOptionMutation("configuration.voice-consent")) { return; }
        var origin = OriginalOrigin();
        var callRevision = CallPolicyRevision;

        // Withdrawal closes capture even if persisting the preference subsequently fails.
        if (!consent)
        {
            voiceConsent = false;
            OnPropertyChanged(nameof(HasVoiceConsent));
            OnPropertyChanged(nameof(NeedsVoiceConsent));
            HoldVoiceInput("Microphone closed · ongoing voice consent withdrawn");
        }

        try
        {
            voiceConsentPreferences.Save(consent);
            voiceConsent = consent;
            OnPropertyChanged(nameof(HasVoiceConsent));
            OnPropertyChanged(nameof(NeedsVoiceConsent));
            NotifyVoiceEnablementChanged();
            if (consent)
            {
                await StartListeningAsync(origin, callRevision);
            }
            else
            {
                await StopListeningAsync();
                ShowInformation("Voice consent declined or withdrawn.",
                    "The microphone remains closed across restart. Native and typed controls remain available.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ApplicationLog.Error(logger, exception, "Saving ongoing voice consent");
            ShowFailure("Voice consent could not be saved.",
                "Capture remains closed. Review the storage error before restarting: " + exception.Message);
            HoldVoiceInput("Microphone closed · consent persistence failed");
        }
    }

    public async Task BeginPushToTalkAsync()
    {
        if (!IsVoiceEnabled || lifecycleAdmissionClosed || IsBusy && !IsSpeaking
            || !sessionController.IsCurrentSessionUnlocked())
        {
            ApplicationLog.Information(logger, "Denied push-to-talk without explicit eligible listening enablement");
            ShowInformation("Push-to-talk is unavailable.", "Review voice consent/readiness and use Enable listening first.");
            return;
        }
        if (Interlocked.Exchange(ref pushToTalkHeld, 1) != 0)
        {
            return;
        }
        Interlocked.Exchange(ref acceptedTranscriptGeneration, -1);

        var revision = Interlocked.Read(ref voiceRecoveryRevision);
        using var opening = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        captureOpenCancellation = opening;
        var ownsBusyState = !IsBusy;
        long generationToAccept = -1;
        if (ownsBusyState)
        {
            IsBusy = true;
        }
        try
        {
            MicrophoneAccessStatus = microphoneAccessService.GetStatus();
            var privacy = privacyObservation.Refresh();
            if (EffectiveMicrophone is null || MicrophoneAccessStatus.State != MicrophoneAccessState.Allowed
                || !IsVoiceActivationAvailable || !privacy.CanCaptureFrom(SelectedMicrophone!))
            {
                throw new InvalidOperationException("The selected microphone, permission or voice policy is unavailable.");
            }

            textToSpeech.InvalidateOutput();
            await textToSpeech.StopAsync(opening.Token);
            IsSpeaking = false;
            activeSpokenText = null;
            await voiceRecognition.BeginPushToTalkAsync(SelectedMicrophone!, GetRecognitionPhrases(), AssistantName, opening.Token);
            if (revision != Interlocked.Read(ref voiceRecoveryRevision) || !IsVoiceEnabled
                || Volatile.Read(ref pushToTalkHeld) == 0 || !sessionController.IsCurrentSessionUnlocked())
            {
                voiceRecognition.InvalidateCapture();
                await TryStopFailedCaptureAsync("Closing an ineligible push-to-talk activation");
                return;
            }

            Interlocked.Exchange(ref acceptedTranscriptGeneration, voiceRecognition.Generation);
            IsListening = voiceRecognition.IsListening;
            BeginPushToTalkCommand.NotifyCanExecuteChanged();
            PresentResponse(AssistantState.Listening, "Capturing your command.",
                "Release Push to talk to finish. Only this explicitly activated command is transcribed locally.");
            generationToAccept = voiceRecognition.Generation;
        }
        catch (OperationCanceledException) when (opening.IsCancellationRequested)
        {
            HoldVoiceInput("Microphone closed · capture open cancelled or timed out");
            if (await TryStopFailedCaptureAsync("Closing a cancelled push-to-talk activation"))
            {
                ShowInformation("Command capture did not start.", "Use Enable listening and try push-to-talk again.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException
            or IOException or UnauthorizedAccessException)
        {
            HoldVoiceInput("Microphone closed · capture failed");
            ApplicationLog.Error(logger, exception, "Opening explicit push-to-talk capture");
            if (await TryStopFailedCaptureAsync("Closing a failed push-to-talk activation"))
            {
                ShowFailure(exception is ArgumentOutOfRangeException
                    ? "The selected microphone is unavailable."
                    : "Windows speech recognition is unavailable.", exception.Message);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HoldVoiceInput("Microphone closed · unexpected capture failure");
            ApplicationLog.Error(logger, exception, "Opening explicit push-to-talk capture unexpectedly");
            if (await TryStopFailedCaptureAsync("Closing an unexpectedly failed push-to-talk activation"))
            {
                ShowFailure("Voice capture failed.", exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(captureOpenCancellation, opening))
            {
                captureOpenCancellation = null;
            }
            if (ownsBusyState)
            {
                IsBusy = false;
            }
        }
        if (generationToAccept >= 0 && !voiceRecognition.AcceptCaptureGeneration(generationToAccept))
        {
            HoldVoiceInput("Microphone closed · activation was retired before acknowledgement");
            if (await TryStopFailedCaptureAsync("Closing a retired push-to-talk activation"))
            {
                ShowInformation("Command activation is no longer current.",
                    "No buffered command was accepted. Use Enable listening and activate push-to-talk again.");
            }
        }
    }

    private async Task<bool> TryStopFailedCaptureAsync(string operation)
    {
        try
        {
            await voiceRecognition.StopAsync();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, operation);
            ShowFailure("Microphone cleanup needs attention.",
                exception.Message + " Restart Kora before using voice again.");
            return false;
        }
    }

    public async Task EndPushToTalkAsync()
    {
        Interlocked.Exchange(ref pushToTalkHeld, 0);
        if (captureOpenCancellation is { } opening)
        {
            await opening.CancelAsync();
            return;
        }
        if (!IsListening)
        {
            return;
        }

        try
        {
            await voiceRecognition.EndPushToTalkAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HoldVoiceInput("Microphone closed · command completion failed");
            ApplicationLog.Error(logger, exception, "Completing explicit push-to-talk");
            ShowFailure("Command capture failed.", exception.Message);
        }
        finally
        {
            IsListening = false;
            NotifyVoiceEnablementChanged();
        }
    }

    public async Task SelectMicrophoneAsync(MicrophoneDevice microphone, long topologyRevision)
    {
        if (disposed) { return; }
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Recovery);
        var valid = await TryValidateMicrophoneRecoveryAsync(topologyRevision, microphone);
        if (disposed) { activity.Complete(HostOperationOutcome.Failed); return; }
        if (!valid || !Microphones.Contains(microphone))
        {
            ApplicationLog.Information(logger, "Rejected stale or ineligible native microphone selection");
            ShowFailure("The microphone selection is no longer current.", "Refresh devices and choose an endpoint again.");
            OnPropertyChanged(nameof(SelectedMicrophone));
            activity.Complete(HostOperationOutcome.Failed);
            return;
        }

        SelectedMicrophone = microphone;
        var released = IsVoiceEnabled || await TryStopFailedCaptureAsync("Releasing input after native microphone selection");
        activity.Complete(released && Equals(SelectedMicrophone, microphone)
            ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
    }

    private async Task<bool> TryValidateMicrophoneRecoveryAsync(long revision, MicrophoneDevice? candidate = null)
    {
        try
        {
            var snapshot = await microphoneCatalog.RefreshAsync(CancellationToken.None);
            var privacy = snapshot.Privacy;
            return !disposed && IsCallMutationHostEligible && !IsBusy && !IsRefreshingMicrophones
                && microphoneCatalogCurrent && revision == MicrophoneTopologyRevision
                && privacy.TopologyRevision == catalogPrivacyRevision
                && privacy.TopologyRevision == privacyObservation.Current.TopologyRevision
                && privacy.MicrophoneAccess == MicrophoneAccessState.Allowed
                && snapshot.Access.State == MicrophoneAccessState.Allowed
                && (candidate?.IsSystemDefault == true
                    || (candidate ?? SelectedMicrophone) is { } microphone && privacy.CanCaptureFrom(microphone)
                    && privacyObservation.Current.CanCaptureFrom(microphone));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (disposed) { return false; }
            microphoneCatalogCurrent = false;
            HoldVoiceInput("Microphone closed · privacy observation failed");
            ApplicationLog.Error(logger, exception, "Validating native microphone recovery");
            ShowFailure("Microphone recovery needs attention.", "Refresh devices after checking Windows privacy.");
            return false;
        }
    }

    public async Task EnableListeningFromTrayAsync(long revision)
    {
        if (disposed) { return; }
        var valid = await TryValidateMicrophoneRecoveryAsync(revision);
        if (disposed) { return; }
        if (!valid)
        {
            ShowFailure("Listening cannot be enabled.", "The menu or privacy state changed. Refresh devices and review voice consent in Settings.");
            return;
        }
        if (!IsVoiceEnabled)
        {
            await ToggleListeningAsync();
        }
    }

    public Task DisableListeningFromTrayAsync()
    {
        if (disposed) { return Task.CompletedTask; }
        // A stale Disable must never become Enable. Stop remains available during busy work.
        return DisableListeningAsync();
    }

    private async Task DisableListeningAsync()
    {
        HoldVoiceInput("Microphone closed · listening was disabled manually");
        if (!await TryStopFailedCaptureAsync("Disabling voice activation"))
        {
            return;
        }
        ApplicationLog.Information(logger, "Voice activation was disabled by the user");
        ShowInformation("Listening disabled.", "The microphone capture device has been released.");
    }

    public Task StopSpeakingFromTrayAsync() => disposed ? Task.CompletedTask : StopSpeakingAsync();

    public Task RefreshMicrophonesAsync()
    {
        if (disposed) { return Task.CompletedTask; }
        if (IsRefreshingMicrophones) { return microphoneRefreshTask; }
        return microphoneRefreshTask = RefreshMicrophonesCoreAsync();
    }

    private async Task RefreshMicrophonesCoreAsync()
    {
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Recovery);
        using var cancellation = new CancellationTokenSource();
        microphoneRefreshCancellation = cancellation;
        var recoveryRevision = Interlocked.Read(ref voiceRecoveryRevision);
        isRefreshingMicrophones = true;
        microphoneCatalogCurrent = false;
        Interlocked.Increment(ref microphoneTopologyRevision);
        OnPropertyChanged(nameof(IsRefreshingMicrophones));
        OnPropertyChanged(nameof(MicrophoneTopologyRevision));
        NotifyVoiceEnablementChanged();
        try
        {
            var previousMicrophone = selectedMicrophone;
            var snapshot = await microphoneCatalog.RefreshAsync(cancellation.Token);
            if (disposed || recoveryRevision != Interlocked.Read(ref voiceRecoveryRevision))
            {
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            var privacy = snapshot.Privacy;
            if (!IsCallMutationHostEligible || privacy.SessionState != WindowsSessionState.Unlocked)
            {
                HoldVoiceInput("Microphone closed · native recovery is ineligible");
                ShowFailure("Microphone recovery is unavailable.", "Return to the owning unlocked host and refresh devices.");
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            catalogPrivacyRevision = privacy.TopologyRevision;
            systemDefaultMicrophone = snapshot.DefaultMicrophone;
            suppressAudioDevicePreferenceSave = true;
            try
            {
                Microphones.Clear();
                Microphones.Add(SystemAudioDevices.Microphone);
                foreach (var device in snapshot.Devices)
                {
                    Microphones.Add(device);
                }
                if (previousMicrophone is not null)
                {
                    SelectedMicrophone = Microphones.FirstOrDefault(device =>
                        string.Equals(device.Id, previousMicrophone.Id, StringComparison.Ordinal)) ?? previousMicrophone;
                }
            }
            finally
            {
                suppressAudioDevicePreferenceSave = false;
            }
            MicrophoneAccessStatus = snapshot.Access;
            microphoneCatalogCurrent = true;
            UpdateMicrophoneAvailability(selectedMicrophoneUnavailable: EffectiveMicrophone is null);
            if (IsVoiceEnabled && (EffectiveMicrophone is null || !privacy.CanCaptureFrom(SelectedMicrophone!)))
            {
                HoldVoiceInput("Microphone closed · endpoint or permission unavailable");
                await TryStopFailedCaptureAsync("Releasing unavailable input after metadata refresh");
            }
            OnPropertyChanged(nameof(MicrophoneTopologyRevision));
            NotifyVoiceEnablementChanged();
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            activity.Complete(HostOperationOutcome.Failed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (disposed) { activity.Complete(HostOperationOutcome.Failed); return; }
            HoldVoiceInput("Microphone closed · device enumeration failed");
            ApplicationLog.Error(logger, exception, "Refreshing native microphone recovery");
            await TryStopFailedCaptureAsync("Releasing input after metadata refresh failure");
            ShowFailure("Microphone recovery needs attention.",
                "Refresh devices to retry, or review voice consent and Windows privacy in Settings. Failure type: " + exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            microphoneRefreshCancellation = null;
            isRefreshingMicrophones = false;
            if (!disposed)
            {
                OnPropertyChanged(nameof(IsMicrophoneCatalogCurrent));
                OnPropertyChanged(nameof(IsRefreshingMicrophones));
                NotifyVoiceEnablementChanged();
            }
        }
    }

    public async Task<bool> TryPrepareHandoffAsync()
    {
        if (lifecycleAdmissionClosed || IsBusy || !clipboardPreview.IsQuiescent || IsLocalModelSetupActive || IsPowerShellSetupActive || IsSpeechProviderOperationActive
            || activeReasoningTask is { IsCompleted: false } || isModelActionDispatchActive
            || !sessionController.IsCurrentSessionUnlocked())
        {
            ApplicationLog.Information(logger, "Handoff is blocked until current work finishes and Windows is eligible");
            return false;
        }

        Interlocked.Exchange(ref handoffPreparationActive, 1);
        lifecycleAdmissionClosed = true;
        ClearClipboardPreview();
        HoldVoiceInput("Microphone closed · host handoff");
        textToSpeech.InvalidateOutput();
        try
        {
            await StopAudioAsync();
            if (voiceRecognition.IsCaptureQuiescent && !textToSpeech.IsSpeaking)
            {
                return true;
            }
            lifecycleAdmissionClosed = false;
            Interlocked.Exchange(ref handoffPreparationActive, 0);
            ShowFailure("Handoff could not confirm quiescence.",
                "The original retains ownership. Audio remains disabled; inspect readiness before trying again.");
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            ApplicationLog.Error(logger, exception, "Quiescing the host for handoff");
            lifecycleAdmissionClosed = false;
            Interlocked.Exchange(ref handoffPreparationActive, 0);
            ShowFailure("Handoff could not release host resources.", exception.Message);
            return false;
        }

    }

    internal void AbandonHandoffPreparation()
    {
        if (Interlocked.Exchange(ref handoffPreparationActive, 0) == 0 || hostExitRequested)
        {
            return;
        }
        lifecycleAdmissionClosed = false;
        ApplicationLog.Information(logger, "Handoff preparation was abandoned; original ownership and native recovery are retained");
        NotifyVoiceEnablementChanged();
        ShowInformation("Handoff was not completed.",
            "The original keeps ownership. Input remains disabled; use Enable listening explicitly when ready.");
    }
}