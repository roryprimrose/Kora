using Kora.Application.Communication;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly CallCommunicationPolicy communicationPolicy;
    private Func<bool> callOwnershipEligible = static () => false;
    private Task callClosureTask = Task.CompletedTask;

    public bool IsManualCallActive => communicationPolicy.Current.ManualActive;
    public long CallPolicyRevision => communicationPolicy.Current.Revision;
    public CallState AutomaticCallState => communicationPolicy.Current.AutomaticState;
    public bool IsProtectedCall => communicationPolicy.Current.IsProtected;
    public bool AreReusableGrantsIgnored => !communicationPolicy.Current.Authorization(true, true).AllowsReusableGrants;
    public CallPolicyObservation CallObservation => communicationPolicy.Current;
    public bool CanEnableCallVisualProtection => !ShowVisualTextDuringCalls;
    public bool CanDisableCallVoiceActivation => AllowVoiceActivationDuringCalls;
    public string CallManualStatus => IsManualCallActive
        ? "Manual call mode is active for this run. Clearing it does not clear automatic observations."
        : "Manual call mode is off. It is not saved across restart.";
    public string CallProtectionLimitations =>
        "Automatic detection is unavailable in this build. Saved output/activation choices are retained. "
        + "Reusable grants are ignored during protected calls. New speech/activation protection downgrades, "
        + "temporary exceptions and speak-once call exceptions are unavailable: exact trusted review is not implemented.";

    public void BindCallOwnershipGate(Func<bool> gate) => callOwnershipEligible = gate;

    internal Task CallClosureTask => callClosureTask;

    private bool IsCallMutationHostEligible => !disposed && IsHostInputEligible && callOwnershipEligible();

    private static RequestOrigin OriginalOrigin(SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser) =>
        HostActivity.Current?.Request.Origin
        ?? (initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        });

    private bool AdmitVoiceOptionMutation(string action, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var outcome = communicationPolicy.CheckMutation(OriginalOrigin(initiator), CallPolicyRevision,
            () => IsCallMutationHostEligible);
        if (outcome is null) { return true; }
        var audit = StartAudit(SecurityAuditCategory.ConfigurationWrite, action,
            OriginalOrigin(initiator) == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
            DeviceLocalPreferencesTarget);
        CompleteAudit(audit, SecurityAuditOutcome.Denied, outcome.Value.ToString().ToLowerInvariant());
        ReportCallMutation(outcome.Value, action, OriginalOrigin(initiator));
        return false;
    }

    public async Task SetManualCallAsync(bool active, RequestOrigin origin, long observedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Ambient host lineage is authoritative even if a later button supplies LocalUi.
        origin = HostActivity.Current?.Request.Origin ?? origin;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Policy);
        var audit = StartAudit(SecurityAuditCategory.ConfigurationWrite, "configuration.manual-call",
            origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "call.current-run");
        var outcome = communicationPolicy.SetManual(active, origin, observedRevision,
            () => !cancellationToken.IsCancellationRequested && IsCallMutationHostEligible);
        CompleteAudit(audit, outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
            ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied, outcome.ToString().ToLowerInvariant());
        activity.Complete(outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
            ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        ReportCallMutation(outcome, "configuration.manual-call", origin);
#pragma warning disable VSTHRD003 // This mutation synchronously starts the owned transition; await its audio release.
        await callClosureTask;
#pragma warning restore VSTHRD003
    }

    private void ReportCallMutation(CallMutationOutcome outcome, string action, RequestOrigin origin)
    {
        if (outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged or CallMutationOutcome.PersistenceFailed)
        {
            return;
        }
        ApplicationLog.CallMutationRejected(logger,
            action, origin, outcome, CallPolicyRevision);
        ShowInformation("Call-sensitive change was not applied.", outcome switch
        {
            CallMutationOutcome.ExactReviewUnavailable => "This protection downgrade requires complete exact trusted review, which is unavailable. The existing preference is unchanged.",
            CallMutationOutcome.OriginDenied => "Voice-originated or unknown-origin voice/in-call changes are not permitted during a protected call. Start a new change in Settings; a later confirmation cannot relabel the original request.",
            CallMutationOutcome.StaleObservation => "Call policy changed. Inspect the current status and initiate a new change; nothing is queued for later.",
            _ => "The current host ownership or Windows privacy state does not permit this change.",
        });
    }

    private async void OnCommunicationPolicyChanged(object? sender, EventArgs args)
    {
        try
        {
            var observation = communicationPolicy.Current;
            ApplicationLog.CallStateChanged(logger, observation.EffectiveState);
            // Invalidation precedes any dispatcher or asynchronous stop, including pending synthesis.
            if (observation.SuppressSpeech) { textToSpeech.InvalidateOutput(); }
            if (!observation.AllowActivation && IsVoiceEnabled)
            {
                HoldVoiceInput("Microphone closed · voice activation paused during detected call");
            }
            var transition = CompleteCallTransitionAsync(observation);
            if (observation.Revision == CallPolicyRevision) { callClosureTask = transition; }
            await transition;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Applying the call-aware output policy");
            if (!disposed) { ShowFailure("The call-aware output policy could not be applied.", exception.Message); }
        }
    }

    private async Task CompleteCallTransitionAsync(CallPolicyObservation observation)
    {
        var output = observation.SuppressSpeech ? textToSpeech.StopAsync() : Task.CompletedTask;
        var capture = !observation.AllowActivation ? voiceRecognition.StopAsync() : Task.CompletedTask;
        await uiDispatcher.InvokeAsync(() =>
        {
            if (!disposed && observation.Revision == CallPolicyRevision)
            {
                CurrentCallState = observation.EffectiveState;
                ShowVisualTextDuringCalls = observation.Settings.ShowVisualTextDuringCalls;
                AllowVoiceActivationDuringCalls = observation.Settings.AllowVoiceActivationDuringCalls;
                OnPropertyChanged(nameof(IsManualCallActive));
                OnPropertyChanged(nameof(CallManualStatus));
                OnPropertyChanged(nameof(CallPolicyRevision));
                OnPropertyChanged(nameof(AutomaticCallState));
                OnPropertyChanged(nameof(CallStateStatus));
                OnPropertyChanged(nameof(IsProtectedCall));
                OnPropertyChanged(nameof(AreReusableGrantsIgnored));
                OnPropertyChanged(nameof(CanEnableCallVisualProtection));
                OnPropertyChanged(nameof(CanDisableCallVoiceActivation));
                GrantDocumentChanged?.Invoke(this, GetGrantDocument());
                NotifyOutputPolicyChanged();
                if (observation.SuppressSpeech)
                {
                    IsSpeaking = false;
                    activeSpokenText = null;
                }
                if (observation.SuppressSpeech && CanRevealPrivatePresentation)
                {
                    WindowActionRequested?.Invoke(this, WindowAction.Show);
                }
            }
            return Task.CompletedTask;
        });
        await Task.WhenAll(output, capture);
    }

    private async Task SetCallSettingsAsync(CallAwareSettings settings)
    {
        var origin = OriginalOrigin();
        var revision = CallPolicyRevision;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Policy);
        var audit = StartAudit(SecurityAuditCategory.ConfigurationWrite, CallAwareConfigurationAction,
            origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        CallMutationOutcome outcome;
        try
        {
            outcome = communicationPolicy.SetSettings(settings, origin, revision,
                () => IsCallMutationHostEligible, value =>
                {
                    callAwarePreferences.Save(value);
                    return true;
                });
            CompleteAudit(audit, outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied, outcome.ToString().ToLowerInvariant());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            outcome = CallMutationOutcome.PersistenceFailed;
            CompleteAudit(audit, SecurityAuditOutcome.Failed,
                exception is UnauthorizedAccessException ? "access-denied" : "io-error");
            ApplicationLog.Error(logger, exception, "Saving call-aware settings");
            ShowFailure("The call-aware settings could not be saved.", exception.Message);
        }
        ReportCallMutation(outcome, CallAwareConfigurationAction, origin);
        activity.Complete(outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
            ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
#pragma warning disable VSTHRD003 // This mutation synchronously starts the owned transition; await its audio release.
        await callClosureTask;
#pragma warning restore VSTHRD003
    }

    private bool CanReuseLegacyModelGrant(BuiltInAction action) =>
        IsCallMutationHostEligible && communicationPolicy.Current.Authorization(true, true).AllowsReusableGrants
        && (sessionAllowedModelActions.Contains(action) || alwaysAllowedModelActions.Contains(action));

    private bool IsModelCallDispatchEligible(SecurityAuditInitiator initiator, long? observedCallRevision) =>
        initiator != SecurityAuditInitiator.ModelSuggestion
        || IsCallMutationHostEligible && observedCallRevision is { } revision && revision == CallPolicyRevision;
}
