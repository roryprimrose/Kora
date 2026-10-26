using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Communication;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly CommunicationActivityCallStateService? communicationActivity;
    private readonly CommunicationActivityConfigurationService? communicationActivityConfiguration;
    private bool communicationActivityApplying;

    public AsyncCommand InspectCommunicationActivityCommand { get; }
    public AsyncCommand EnableCommunicationActivityCommand { get; }
    public AsyncCommand DisableCommunicationActivityCommand { get; }
    public bool CanEnableCommunicationActivity => communicationActivity is { IsQualified: true, PreferencesAvailable: true }
        && CanChangeCommunicationActivity;
    public bool CanChangeCommunicationActivity => communicationActivityConfiguration is not null
        && !communicationActivityApplying && IsCallMutationHostEligible && !IsResponseInteractionPending
        && Volatile.Read(ref manualCallNativeAvailable) == 1;

    public string CommunicationActivityStatus
    {
        get
        {
            if (communicationActivity is null) { return "Automatic communications observations are unavailable."; }
            var current = communicationActivity;
            var observed = current.Observation;
            return $"Selected source: {current.Selection}. Support: {(current.IsQualified ? "qualified" : "unavailable - no qualified Windows profile")}. "
                + $"Automatic policy evidence: {current.CurrentState}. "
                + (current.PreferencesAvailable ? "" : "Saved preference evidence is unconfirmed; speech remains protected. ")
                + (current.ResourceReleaseFailed ? "Source release failed; retired resources remain quarantined. " : "")
                + (observed is null ? "No current observation. " : $"Observed UTC: {observed.ObservedAt:O}; health: {observed.Health}; maximum age: 5 seconds. ")
                + "Communications audio is at most Suspected, never proof of a Teams or actual call. "
                + "Startup and ambiguous/multiple-stream/device/session loss remain Unknown. "
                + "Clear requires a witnessed single non-Kora communications stream ending within the observed device epoch. "
                + "No audio/content recording, microphone permission, account access or network is requested.";
        }
    }

    private void OnCommunicationActivityChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() =>
        {
            if (disposed) { return; }
            OnPropertyChanged(nameof(CommunicationActivityStatus));
            OnPropertyChanged(nameof(CanEnableCommunicationActivity));
            OnPropertyChanged(nameof(CanChangeCommunicationActivity));
        });

    internal async Task ExecuteCommunicationActivityAsync(CommunicationActivityCommand command,
        SecurityAuditInitiator initiator)
    {
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify the communications observation setting.", command.Error!);
            return;
        }
        if (command.Operation is AppearanceCommandOperation.Get or AppearanceCommandOperation.List)
        {
            if (IsResponseInteractionPending) { Transcript = CommunicationActivityStatus; return; }
            PresentResponse(AssistantState.Information, "Communications activity observations.", CommunicationActivityStatus, refreshOutput: false);
            forceVisualResponse = true;
            NotifyOutputPolicyChanged();
            return;
        }
        if (communicationActivityConfiguration is null || !IsCallMutationHostEligible
            || IsResponseInteractionPending || communicationActivityApplying
            || initiator == SecurityAuditInitiator.LocalUser && Volatile.Read(ref manualCallNativeAvailable) != 1)
        {
            Transcript = "Communication source selection requires the current owning unlocked host and a fresh Settings or typed/activated request; nothing was saved.";
            return;
        }
        var origin = OriginalOrigin(initiator);
        if (!AdmitVoiceOptionMutation("configuration.communication-activity", initiator)) { return; }
        var input = CaptureManualCallInput(origin, CallPolicyRevision);
        var nativeRevision = Interlocked.Read(ref manualCallNativeRevision);
        bool Current() => input.Eligible() && !IsResponseInteractionPending && (initiator != SecurityAuditInitiator.LocalUser
            || nativeRevision == Interlocked.Read(ref manualCallNativeRevision)
                && Volatile.Read(ref manualCallNativeAvailable) == 1);
        var revision = CallPolicyRevision;
        communicationActivityApplying = true;
        try
        {
            var result = await communicationActivityConfiguration.SelectAsync(command.Selection, origin,
                revision, communicationPolicy, Current, CancellationToken.None);
            Transcript = $"{result}. {CommunicationActivityStatus}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Selecting communications activity observations");
            if (!disposed) { Transcript = "Communication activity selection was not confirmed. Inspect preference and resource evidence in Settings. " + CommunicationActivityStatus; }
        }
        finally
        {
            communicationActivityApplying = false;
            OnCommunicationActivityChanged(this, EventArgs.Empty);
        }
    }
}