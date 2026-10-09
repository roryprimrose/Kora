using System.Collections.Immutable;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly InputDevicePreferenceService inputDevicePreferences;
    private bool committingMicrophonePreference;

    private void SetSelectedMicrophoneState(MicrophoneDevice? value)
    {
        if (SetProperty(ref selectedMicrophone, value))
        {
            ToggleListeningCommand.NotifyCanExecuteChanged();
            UpdateMicrophoneAvailability(selectedMicrophoneUnavailable: false);
        }
    }

    private void ApplyDirectMicrophonePreference(MicrophoneDevice value)
    {
        if (string.Equals(selectedMicrophone?.Id, value.Id, StringComparison.Ordinal)) { return; }
        var origin = OriginalOrigin();
        using var activity = HostActivity.Current is not null
            ? HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage)
            : HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Storage);
        var saved = CommitMicrophonePreference(value, MicrophoneTopologyRevision, CallPolicyRevision,
            origin, SecurityAuditInitiator.LocalUser, activity.Request,
            Interlocked.Read(ref voiceRecoveryRevision), CancellationToken.None);
        activity.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
    }

    internal async Task ExecuteInputDeviceCommandAsync(InputDeviceCommand command,
        SecurityAuditInitiator initiator, CancellationToken cancellationToken = default)
    {
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowInformation("Clarify the input setting.", command.Error!);
            return;
        }
        if (!IsCallMutationHostEligible || pendingModelQuestion is not null
            || IsGrantChangePending || IsModelActionApprovalPending)
        {
            ShowFailure("Input configuration unavailable.", "Use the owning unlocked host and resolve pending questions/approvals first.");
            return;
        }
        // These objects come from the admitted host catalogue, never a friendly-name/provider selector.
        if (command.Operation == AppearanceCommandOperation.List)
        {
            await RefreshInputMetadataAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (disposed) { return; }
        if (!IsCallMutationHostEligible)
        {
            ShowFailure("Input configuration unavailable.", "Ownership or privacy changed. Return explicitly to the owning unlocked host.");
            return;
        }
        if (command.Operation is AppearanceCommandOperation.List or AppearanceCommandOperation.Get)
        {
            PresentInputConfiguration("inspected");
            return;
        }
        var candidate = command.Operation == AppearanceCommandOperation.Reset
            ? SystemAudioDevices.Microphone
            : Microphones.SingleOrDefault(device => string.Equals(device.Id, command.Value, StringComparison.Ordinal));
        if (candidate is null)
        {
            ShowFailure("Exact input choice required.", "Use list input settings, then one exact listed endpoint ID; names, indices and unavailable pins cannot be selected.");
            return;
        }
        var saved = await SelectMicrophonePreferenceAsync(candidate, MicrophoneTopologyRevision,
            initiator, cancellationToken);
        if (!disposed && IsCallMutationHostEligible) { PresentInputConfiguration(saved ? "saved" : "not-confirmed"); }
    }

    private void PresentInputConfiguration(string outcome)
    {
        var current = microphoneCatalogCurrent && !IsRefreshingMicrophones && IsCallMutationHostEligible
            && privacyObservation.Current.TopologyRevision == catalogPrivacyRevision;
        var effective = current && MicrophoneAccessStatus.State == MicrophoneAccessState.Allowed
            && privacyObservation.Current.MicrophoneAccess == MicrophoneAccessState.Allowed
            && SelectedMicrophone is { } desired && privacyObservation.Current.CanCaptureFrom(desired)
            ? EffectiveMicrophone : null;
        var devices = current ? Microphones.ToList() : [];
        if (SelectedMicrophone is { } pin && !devices.Contains(pin)) { devices.Add(pin); }
        var result = new InputDeviceCommandResult(outcome,
            string.Equals(outcome, "not-confirmed", StringComparison.Ordinal) ? ResponseTitle + " " + ResponseBody
                : !current || effective is null ? "Input unavailable. Inspect the saved pin, refresh metadata and review Windows permission separately; no replacement or enabling is automatic." : null,
            MicrophoneTopologyRevision, CallPolicyRevision, current, inputDevicePreferences.DesiredId,
            effective?.Id, inputDevicePreferences.Source, TrayInputStatus)
        {
            Choices = devices.Select(device => new InputDeviceCommandChoice(device.Id, device.Name,
                device.IsSystemDefault, current && (device.IsSystemDefault ? IsSystemMicrophoneAvailable : Microphones.Contains(device))))
                .ToImmutableArray(),
        };
        var body = InputDeviceCommandResult.Serialize(result);
        if (string.Equals(outcome, "not-confirmed", StringComparison.Ordinal)) { ShowFailure("Input preference not confirmed.", body); }
        else { ShowInformation("Input configuration.", body); }
    }

    private async Task<bool> SelectMicrophonePreferenceAsync(MicrophoneDevice microphone, long revision,
        SecurityAuditInitiator initiator, CancellationToken cancellationToken)
    {
        if (disposed) { return false; }
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        {
            ShowFailure("Input preference denied.", "The admitted original voice channel is missing. Submit a fresh request.");
            return false;
        }
        var request = HostActivity.Current?.Request ?? HostRequest.Create(origin);
        var callRevision = CallPolicyRevision;
        var recoveryRevision = Interlocked.Read(ref voiceRecoveryRevision);
        using var activity = HostActivity.Current is not null
            ? HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Recovery)
            : HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Recovery);
        var valid = await TryValidateMicrophoneRecoveryAsync(revision, microphone, cancellationToken);
        if (disposed) { activity.Complete(HostOperationOutcome.Failed); return false; }
        if (!valid || !Microphones.Any(item => ReferenceEquals(item, microphone)))
        {
            ShowFailure("The microphone selection is no longer current.", "Refresh devices and choose an endpoint again.");
            OnPropertyChanged(nameof(SelectedMicrophone));
            activity.Complete(HostOperationOutcome.Failed);
            return false;
        }
        bool saved;
        try
        {
            saved = CommitMicrophonePreference(microphone, revision, callRevision, origin,
                initiator, request, recoveryRevision, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            if (string.Equals(inputDevicePreferences.Source, "unavailable", StringComparison.Ordinal))
            { HoldVoiceInput("Microphone closed · preference audit context failed"); }
            await TryStopFailedCaptureAsync("Releasing input after rejected microphone audit context");
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        var released = IsVoiceEnabled || await TryStopFailedCaptureAsync("Releasing input after microphone preference selection");
        var completed = saved && released;
        activity.Complete(completed ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        return completed;
    }

    private bool CommitMicrophonePreference(MicrophoneDevice microphone, long revision, long callRevision,
        RequestOrigin origin, SecurityAuditInitiator initiator, HostRequest request,
        long recoveryRevision, CancellationToken cancellationToken)
    {
        var saved = false;
        if (committingMicrophonePreference) { return false; }
        committingMicrophonePreference = true;
        try
        {
            var changed = !string.Equals(selectedMicrophone?.Id, microphone.Id, StringComparison.Ordinal);
            var voiceAdmitted = origin != RequestOrigin.ActivatedVoice
                || IsVoiceEnabled && HasVoiceConsent && CallObservation.AllowActivation;
            var expectedRevision = changed ? checked(revision + 1) : revision;
            var expectedRecoveryRevision = changed ? checked(recoveryRevision + 1) : recoveryRevision;
            saved = inputDevicePreferences.Apply(microphone, request, callRevision, initiator, communicationPolicy, () =>
            voiceAdmitted && IsCallMutationHostEligible && !IsBusy
            && !cancellationToken.IsCancellationRequested && microphoneCatalogCurrent
            && !IsRefreshingMicrophones && MicrophoneTopologyRevision == expectedRevision
            && Microphones.Any(item => ReferenceEquals(item, microphone))
            && privacyObservation.Current.TopologyRevision == catalogPrivacyRevision
            && privacyObservation.Current.MicrophoneAccess == MicrophoneAccessState.Allowed
            && MicrophoneAccessStatus.State == MicrophoneAccessState.Allowed
            && (microphone.IsSystemDefault || privacyObservation.Current.CanCaptureFrom(microphone))
            && Interlocked.Read(ref voiceRecoveryRevision) == expectedRecoveryRevision,
            () =>
            {
                if (changed)
                { HoldVoiceInput("Microphone changed · use Enable listening"); }
            });
            if (saved)
            {
                SetSelectedMicrophoneState(microphone);
                Interlocked.Increment(ref microphoneTopologyRevision);
                OnPropertyChanged(nameof(MicrophoneTopologyRevision));
            }
            else
            {
                ShowFailure("Input preference denied.", "The host, original channel, call observation or input metadata changed. Inspect and submit a fresh request.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (string.Equals(inputDevicePreferences.Source, "unavailable", StringComparison.Ordinal))
            { HoldVoiceInput("Microphone closed · preference audit evidence failed"); }
            Kora.Application.Diagnostics.ApplicationLog.Error(logger, exception, "Committing the microphone preference or audit evidence");
            ShowFailure("The microphone preference could not be saved.", "The preference or its audit evidence failed. Inspect saved state before retrying; a replacement may already have committed. No rollback or automatic enabling is claimed.");
        }
        finally { committingMicrophonePreference = false; }
        return saved;
    }
}
