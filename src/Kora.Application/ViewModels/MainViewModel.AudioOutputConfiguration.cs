using Kora.Application.Configuration;
using Kora.Core;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly OutputDeviceConfigurationService? outputConfiguration;
    private OutputDeviceChoice? selectedOutputChoice;
    private bool synchronizingOutput;
    private bool outputControlActive;

    public AsyncCommand RefreshOutputDevicesCommand { get; }
    public AsyncCommand SaveOutputDeviceCommand { get; }
    public AsyncCommand ResetOutputDeviceCommand { get; }
    public IReadOnlyList<OutputDeviceChoice> OutputDeviceChoices => outputConfiguration?.Choices ?? [];
    public bool CanChangeAudioOutputDevice => outputConfiguration is not null && IsCallMutationHostEligible
        && !outputControlActive && !IsResponseInteractionPending;
    public OutputDeviceChoice? SelectedOutputChoice
    {
        get => selectedOutputChoice;
        set => SetProperty(ref selectedOutputChoice, value);
    }
    public string AudioOutputConfigurationStatus => outputConfiguration is null
        ? "Audio control admission is unavailable; existing saved routing is retained."
        : OutputDeviceCommandResult.Serialize(outputConfiguration.Get(CallPolicyRevision));

    private void OnOutputConfigurationChanged(object? sender, EventArgs args)
    {
        if (IsSpeaking)
        {
            forceVisualResponse = true;
        }
        uiDispatcher.Post(SynchronizeOutputConfiguration);
    }

    private void SynchronizeOutputConfiguration()
    {
        if (disposed || outputConfiguration is null) { return; }
        var current = outputConfiguration.Get(CallPolicyRevision);
        synchronizingOutput = true;
        try
        {
            OutputDevices.Clear();
            OutputDevices.Add(SystemAudioDevices.Output);
            foreach (var endpoint in outputConfiguration.Metadata?.Devices ?? [])
            {
                OutputDevices.Add(endpoint);
            }
            systemDefaultOutputDevice = outputConfiguration.Metadata?.Default;
            var device = current.Desired is { } id
                ? string.Equals(id, SystemAudioDevices.Output.Id, StringComparison.Ordinal) ? SystemAudioDevices.Output
                    : OutputDevices.FirstOrDefault(endpoint => string.Equals(endpoint.Id, id, StringComparison.Ordinal))
                        ?? new AudioOutputDevice(id, "Unavailable saved output")
                : null;
            SetOutputDeviceSnapshot(device);
            SelectedOutputChoice = outputConfiguration.Choices.FirstOrDefault(choice => string.Equals(choice.Id, current.Desired, StringComparison.Ordinal));
            OnPropertyChanged(nameof(OutputDeviceChoices));
            OnPropertyChanged(nameof(AudioOutputConfigurationStatus));
            OnPropertyChanged(nameof(CanChangeAudioOutputDevice));
            OutputDeviceAvailabilityMessage = current.Recovery ?? "Exact saved output route is ready for the next eligible speech; audibility is not guaranteed.";
        }
        finally { synchronizingOutput = false; }
        NotifyOutputPolicyChanged();
    }

    private Func<bool> CaptureAudioControlEligibility(RequestOrigin origin)
    {
        var call = CallPolicyRevision;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var privacy = privacyObservation.Current;
        return () => IsCallMutationHostEligible && CallPolicyRevision == call
            && privacyObservation.Current.SessionState == WindowsSessionState.Unlocked
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
            && !IsResponseInteractionPending
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && privacyObservation.Current.CanCapture
                && Interlocked.Read(ref voiceRecoveryRevision) == recovery);
    }

    private async Task RefreshOutputConfigurationAsync() =>
        await RunOutputControlAsync(async (service, origin, eligible) =>
            await service.RefreshAsync(origin, eligible, CancellationToken.None));

    private async Task SaveOutputChoiceAsync(bool reset)
    {
        await RunOutputControlAsync(async (service, origin, eligible) =>
        {
            if (reset) { await service.RefreshAsync(origin, eligible, CancellationToken.None); }
            var choice = reset
                ? service.Choices.FirstOrDefault(item => item.Device.IsSystemDefault)
                : SelectedOutputChoice;
            if (choice is null) { throw new InvalidOperationException("Refresh output metadata and choose one exact presented endpoint."); }
            if (!await service.SelectAsync(choice, CallPolicyRevision, origin, SecurityAuditInitiator.LocalUser,
                communicationPolicy, eligible, CancellationToken.None))
            {
                throw new InvalidOperationException("The original channel or current call policy denies this output preference change.");
            }
        });
    }

    private async Task RunOutputControlAsync(Func<OutputDeviceConfigurationService, RequestOrigin, Func<bool>, Task> operation)
    {
        if (outputControlActive || disposed || outputConfiguration is null || !IsCallMutationHostEligible || IsResponseInteractionPending)
        {
            if (!disposed) { Transcript = "Output control requires the current owning unlocked host and an idle admitted audio workflow."; }
            return;
        }
        var origin = OriginalOrigin();
        var eligible = CaptureAudioControlEligibility(origin);
        var preserveResponse = IsSpeaking;
        outputControlActive = true;
        OnPropertyChanged(nameof(CanChangeAudioOutputDevice));
        try
        {
            await operation(outputConfiguration, origin, eligible);
            if (IsSpeaking) { await textToSpeech.StopAsync(CancellationToken.None); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or OperationCanceledException or TimeoutException)
        {
            outputConfiguration.HoldUnavailable("Output preference or its admission/evidence was not confirmed. Inspect saved state and refresh; no automatic retry or substitution. " + exception.GetType().Name);
            if (!disposed) { ReportOutputControlFailure(exception, preserveResponse); }
        }
        finally
        {
            outputControlActive = false;
            SynchronizeOutputConfiguration();
        }
    }

    internal async Task ExecuteOutputDeviceCommandAsync(OutputDeviceCommand command, SecurityAuditInitiator initiator,
        CancellationToken cancellationToken = default)
    {
        if (outputConfiguration is null || !IsCallMutationHostEligible || IsResponseInteractionPending)
        {
            Transcript = "Output control requires the owning unlocked host, admitted audio session and no pending question/approval.";
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify the output setting.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && HostActivity.Current is null) { origin = RequestOrigin.ActivatedVoice; }
        var eligible = CaptureAudioControlEligibility(origin);
        var preserveResponse = IsSpeaking;
        try
        {
            if (command.Operation is AppearanceCommandOperation.List or AppearanceCommandOperation.Get or AppearanceCommandOperation.Reset)
            {
                await outputConfiguration.RefreshAsync(origin, eligible, cancellationToken);
            }
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var id = command.Operation == AppearanceCommandOperation.Reset ? SystemAudioDevices.Output.Id : command.Value;
                var choice = outputConfiguration.Choices.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Use one exact presented endpoint ID; names, indices and unavailable pins cannot select output.");
                saved = await outputConfiguration.SelectAsync(choice, CallPolicyRevision, origin, initiator,
                    communicationPolicy, eligible, cancellationToken);
            }
            if (!IsCallMutationHostEligible || saved && !eligible()) { return; }
            if (saved && IsSpeaking) { await textToSpeech.StopAsync(CancellationToken.None); }
            SynchronizeOutputConfiguration();
            var result = outputConfiguration.Get(CallPolicyRevision,
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied" : "observed");
            var resultText = OutputDeviceCommandResult.Serialize(result);
            // Configuration inspection/mutation never speaks its result or answers a pending question.
            if (preserveResponse || IsResponseInteractionPending)
            {
                Transcript = resultText;
                PreserveSpokenResponseFailure("Output preference result is in the transcript/status; the complete interrupted response remains visual.");
            }
            else if (string.Equals(result.Outcome, "denied", StringComparison.Ordinal)) { ShowFailure("Output preference denied.", resultText); }
            else
            {
                PresentResponse(AssistantState.Information, "Output configuration.", resultText,
                    refreshOutput: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or OperationCanceledException or TimeoutException)
        {
            outputConfiguration.HoldUnavailable("Output preference or evidence not confirmed. Refresh and inspect saved state before a fresh request.");
            if (!disposed) { ReportOutputControlFailure(exception, preserveResponse); }
        }
    }

    private void ReportOutputControlFailure(Exception exception, bool preserveResponse)
    {
        if (preserveResponse || IsResponseInteractionPending)
        {
            Transcript = "Output preference not confirmed. " + exception.Message;
            PreserveSpokenResponseFailure("Output preference was not confirmed; the complete interrupted response remains visual.");
        }
        else { ShowFailure("Output preference not confirmed.", exception.Message); }
    }

    private void SetOutputDeviceSnapshot(AudioOutputDevice? value)
    {
        if (SetProperty(ref selectedOutputDevice, value, nameof(SelectedOutputDevice)))
        {
            PreviewVoiceCommand.NotifyCanExecuteChanged();
            NotifyOutputPolicyChanged();
            UpdateOutputDeviceAvailability();
        }
    }
}