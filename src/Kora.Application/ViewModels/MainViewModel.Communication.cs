using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
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
    private readonly ManualCallControl? manualCallControl;
    private bool manualCallControlActive;
    private long manualCallSpeechRevision;
    private long manualCallNativeRevision;
    private int manualCallNativeAvailable;

    public void BindManualCallNativeLifetime(bool available)
    {
        Volatile.Write(ref manualCallNativeAvailable, available ? 1 : 0);
        Interlocked.Increment(ref manualCallNativeRevision);
        OnPropertyChanged(nameof(CanEnableCommunicationActivity));
        OnPropertyChanged(nameof(CanChangeCommunicationActivity));
    }

    private sealed record ManualCallInput(RequestOrigin Origin, long Revision, Func<bool> Eligible, Func<bool> RetiredEligible)
    {
        internal long NavigationOwnerRevision { get; init; }
        internal bool NavigationInitiallyAdmitted { get; init; }
        internal long AssistantNameRevision { get; init; }
        internal long OwnershipBindingRevision { get; init; }
        internal long InputRevision { get; init; }
        internal AttachmentCataloguePreparationCommand? CataloguePreparation { get; init; }
        internal Func<bool> CatalogueEligible { get; init; } = static () => true;
    }

    public string ManualCallConfigurationStatus => manualCallControl is null
        ? "Manual call controls are unavailable. Call protections remain active."
        : CallStateStatus;
    public Kora.Application.Infrastructure.AsyncCommand GetManualCallStatusCommand { get; }
    public Kora.Application.Infrastructure.AsyncCommand ResetManualCallCommand { get; }

    private ManualCallInput CaptureManualCallInput(RequestOrigin origin, long revision, long? voiceGeneration = null,
        bool native = false)
    {
        origin = HostActivity.Current?.Request.Origin ?? origin;
        var privacy = privacyObservation.Current;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var generation = voiceGeneration ?? voiceRecognition.Generation;
        var capturedOrigin = origin;
        var nativeRevision = Interlocked.Read(ref manualCallNativeRevision);
        bool NativeEligible() => !native || nativeRevision == Interlocked.Read(ref manualCallNativeRevision)
            && Volatile.Read(ref manualCallNativeAvailable) == 1;
        return new(origin, revision, () => IsCallMutationHostEligible && NativeEligible()
            && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
            && Interlocked.Read(ref voiceRecoveryRevision) == recovery
            && (capturedOrigin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && privacyObservation.Current.CanCapture && voiceRecognition.Generation == generation),
            () => IsCallMutationHostEligible && NativeEligible() && privacyObservation.Current.TopologyRevision == privacy.TopologyRevision
                && Interlocked.Read(ref voiceRecoveryRevision) == recovery + 1
                && !IsVoiceEnabled && Interlocked.Read(ref acceptedTranscriptGeneration) == -1)
        {
            NavigationOwnerRevision = auditImpactOwnerRevision(),
            NavigationInitiallyAdmitted = IsCallMutationHostEligible && !IsProtectedCall,
            AssistantNameRevision = assistantNameConfiguration.Get().Revision,
            OwnershipBindingRevision = Interlocked.Read(ref settingsOwnershipBindingRevision),
            InputRevision = Interlocked.Read(ref workspaceSurfaceInputRevision),
        };
    }

    public bool IsManualCallActive => communicationPolicy.Current.ManualActive;
    public long CallPolicyRevision => communicationPolicy.Current.Revision;
    public CallState AutomaticCallState => communicationPolicy.Current.AutomaticState;
    public bool IsProtectedCall => communicationPolicy.Current.IsProtected;
    public bool AreReusableGrantsIgnored => !communicationPolicy.Current.Authorization(true, true).AllowsReusableGrants;
    public CallPolicyObservation CallObservation => communicationPolicy.Current;
    public Kora.Core.Authorization.IHostAuthorizationPolicyGate AuthorizationPolicyGate => communicationPolicy;
    public bool CanEnableCallVisualProtection => !ShowVisualTextDuringCalls;
    public bool CanDisableCallVoiceActivation => AllowVoiceActivationDuringCalls;
    public string CallManualStatus => IsManualCallActive
        ? "Manual call mode is on until you turn it off or restart Kora. Automatic detection is unchanged."
        : "Manual call mode is off. It resets when Kora restarts.";
    public string CallProtectionLimitations =>
        (communicationActivity is { IsQualified: true }
            ? "Optional Windows communications audio supplies supporting observations only, not actual-call identification. "
            : "Automatic call detection is not available in this version. Your saved response and listening choices are kept. ")
        + "Exact grants and the originating session action level remain unchanged during calls. "
        + "Spoken approvals are disabled by default; independent authentication and privacy still apply.";

    public void BindCallOwnershipGate(Func<bool> gate, Func<long>? revision = null)
    {
        RetireCurrentResponseSpeech();
        callOwnershipEligible = gate;
        communicationActivity?.BindHost(() => !disposed && callOwnershipEligible()
            && privacyObservation.Current.SessionState == Kora.Core.Platform.WindowsSessionState.Unlocked,
            () => (revision?.Invoke() ?? 0, privacyObservation.Current.TopologyRevision));
        settingsOwnershipRevision = revision ?? (static () => 0);
        Interlocked.Increment(ref settingsOwnershipBindingRevision);
        UpdateSpeechPreparation();
        auditImpactControlRevision = checked(auditImpactControlRevision + 1);
        auditImpactOwnerRevision = revision ?? (static () => 0);
        auditImpactPreview?.Retire("Owner binding changed; preview retired.");
        Interlocked.Increment(ref diagnosticImpactControlRevision);
        diagnosticImpactPreview?.Retire("Owner binding changed; preview retired.");
        NotifyDiagnosticImpactPreview();
        sessionRetentionPlanner?.Retire("Owner binding changed; preview retired.");
        NotifyAuditImpactPreview();
    }

#pragma warning disable VSTHRD003 // Exposes the owned call transition for asynchronous observation without starting duplicate closure work.
    internal Task CallClosureTask => callClosureTask;
#pragma warning restore VSTHRD003

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
        var origin = OriginalOrigin(initiator);
        ReportCallMutation(outcome.Value, action, origin);
        new AsyncCommand(() => DeniedMutationAudit = RecordDeniedMutationAsync(action, initiator, origin, outcome.Value),
            HandleCommandException).Execute(null);
        return false;
    }

    public Task DeniedMutationAudit { get; private set; } = Task.CompletedTask;

    private async Task RecordDeniedMutationAsync(string action, SecurityAuditInitiator initiator,
        RequestOrigin origin, CallMutationOutcome outcome)
    {
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Policy, origin);
        using var activity = begin();
        var audit = await StartAuditAsync(SecurityAuditCategory.ConfigurationWrite, action,
            origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator, DeviceLocalPreferencesTarget);
        await CompleteAuditAsync(audit, SecurityAuditOutcome.Denied, outcome.ToString().ToLowerInvariant());
        activity.Complete(HostOperationOutcome.Completed);
    }

    internal async Task SetManualCallAsync(bool active, RequestOrigin origin, long observedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(origin)) { throw new ArgumentOutOfRangeException(nameof(origin)); }
        await ExecuteManualCallCommandAsync(new(AppearanceCommandOperation.Set, active),
            CaptureManualCallInput(origin, observedRevision, native: true), cancellationToken);
    }

    private async Task ExecuteManualCallCommandAsync(ManualCallCommand command, ManualCallInput input,
        CancellationToken cancellationToken = default)
    {
        if (disposed || !IsCallMutationHostEligible) { return; }
        if (command.Operation is AppearanceCommandOperation.List or AppearanceCommandOperation.Get)
        {
            // Passive cached inspection never touches intent/audit, speech, question or preview state.
            Transcript = ManualCallCommandResult.Serialize(
                new("observed", manualCallControl is null ? "Manual control admission is unavailable." : null, communicationPolicy.Current));
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            Transcript = command.Error!;
            return;
        }
        if (manualCallControlActive || IsResponseInteractionPending)
        {
            Transcript = "Manual call control is unavailable during a pending question, approval or control transition; no answer or effect was dispatched.";
            return;
        }
        var denial = communicationPolicy.CheckMutation(input.Origin, input.Revision, input.Eligible);
        if (denial is { } denied && (!input.Eligible()
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)))
        {
            ReportCallMutation(denied, "configuration.manual-call", input.Origin);
            return;
        }
        if (manualCallControl is null)
        {
            Transcript = "Manual call control admission is unavailable. The current layer is unchanged.";
            return;
        }
        manualCallControlActive = true;
        try
        {
            var outcome = await manualCallControl.SetAsync(command.Operation != AppearanceCommandOperation.Reset && command.Active == true,
                input.Origin, input.Revision, communicationPolicy,
                () => input.Eligible() && !IsResponseInteractionPending,
                () => RetireManualCallResources(input), cancellationToken);
            if (disposed || !IsCallMutationHostEligible) { return; }
            Transcript = ManualCallCommandResult.Serialize(new(outcome.ToString().ToLowerInvariant(), null, communicationPolicy.Current));
            if (!IsResponseInteractionPending) { ReportCallMutation(outcome, "configuration.manual-call", input.Origin); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Applying run-only manual call control");
            if (!disposed)
            {
                Transcript = ManualCallCommandResult.Serialize(new("not-confirmed",
                    "Required admission, retirement or outcome receipt failed; the current cached process-memory state is shown, not rollback or durable manual state. "
                    + exception.GetType().Name, communicationPolicy.Current));
            }
        }
        finally
        {
            manualCallControlActive = false;
            if (!disposed) { OnPropertyChanged(nameof(ManualCallConfigurationStatus)); }
        }
    }

    private ManualCallRetirement RetireManualCallResources(ManualCallInput input)
    {
        Interlocked.Increment(ref manualCallSpeechRevision);
        textToSpeech.InvalidateOutput();
        HoldVoiceInput("Microphone closed · manual call mode changed; enable listening explicitly when permitted");
        var retirement = Task.WhenAll(textToSpeech.StopAsync(), voiceRecognition.StopAsync());
        uiDispatcher.Post(() =>
        {
            if (disposed) { return; }
            forceVisualResponse = true;
            IsSpeaking = false;
            activeSpokenText = null;
            NotifyOutputPolicyChanged();
        });
        return new(retirement, () => input.RetiredEligible() && !IsResponseInteractionPending);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "CallCommunicationPolicy.Changed requires void; failures are logged and presented without relaxing call protections.")]
    private async void OnCommunicationPolicyChanged(object? sender, EventArgs args)
    {
        try
        {
            auditImpactPreview?.Retire("Call revision changed; preview retired.");
            diagnosticImpactPreview?.Retire("Call revision changed; preview retired.");
            sessionRetentionPlanner?.Retire("Call revision changed; preview retired.");
            RetireSessionCommandReviews();
            RetireQuestionVoiceReply();
            RetireComposerDictation();
            var observation = communicationPolicy.Current;
            RetireCurrentResponseSpeech();
            RetireWebResultDetails();
            speechCaption.Retire();
            uiDispatcher.Post(RetireSpeechCaption);
            ClearClipboardPreview();
            ClearFilePreview();
            ApplicationLog.CallStateChanged(logger, observation.EffectiveState);
            // Invalidation precedes any dispatcher or asynchronous stop, including pending synthesis.
            if (ShouldWithholdCallOutput(observation))
            {
                speechPreparation?.Request(null);
                textToSpeech.InvalidateOutput();
            }
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
            if (!disposed)
            {
                if (IsResponseInteractionPending) { Transcript = "Call-aware output retirement could not be confirmed; the complete pending interaction remains visual."; }
                else { ShowFailure("The call-aware output policy could not be applied.", exception.Message); }
            }
        }
    }

    private async Task CompleteCallTransitionAsync(CallPolicyObservation observation)
    {
        var withhold = ShouldWithholdCallOutput(observation);
        var feedbackRevision = inCallFeedbackConfiguration?.Get().Revision;
        var output = withhold ? textToSpeech.StopAsync() : Task.CompletedTask;
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
                OnPropertyChanged(nameof(ManualCallConfigurationStatus));
                OnPropertyChanged(nameof(CallPolicyRevision));
                OnPropertyChanged(nameof(AutomaticCallState));
                OnPropertyChanged(nameof(CallStateStatus));
                OnPropertyChanged(nameof(IsProtectedCall));
                OnPropertyChanged(nameof(AreReusableGrantsIgnored));
                OnPropertyChanged(nameof(CanEnableCallVisualProtection));
                OnPropertyChanged(nameof(CanDisableCallVoiceActivation));
                GrantDocumentChanged?.Invoke(this, GetGrantDocument());
                NotifyOutputPolicyChanged();
                if (withhold && feedbackRevision == inCallFeedbackConfiguration?.Get().Revision)
                {
                    IsSpeaking = false;
                    activeSpokenText = null;
                }
                if (withhold && CanRevealPrivatePresentation)
                {
                    WindowActionRequested?.Invoke(this, WindowAction.Show);
                }
            }
            return Task.CompletedTask;
        });
        await Task.WhenAll(output, capture);
    }

    private bool ShouldWithholdCallOutput(CallPolicyObservation observation) =>
        observation.SuppressSpeech
        || inCallFeedbackConfiguration is not null && !inCallFeedbackConfiguration.Get().Available
        || InCallFeedbackRules.Resolve(Kora.Core.Voice.ResponseOutputMode.Hybrid,
            DesiredInCallFeedback, observation.EffectiveState) == Kora.Core.Voice.ResponseOutputMode.VisualOnly;

    private async Task SetCallSettingsAsync(CallAwareSettings settings, bool spokenApprovalDowngradeConfirmed = false)
    {
        var origin = OriginalOrigin();
        var revision = CallPolicyRevision;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Policy);
        var audit = await StartAuditAsync(SecurityAuditCategory.ConfigurationWrite, CallAwareConfigurationAction,
            origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            DeviceLocalPreferencesTarget);
        CallMutationOutcome outcome;
        try
        {
            outcome = await Task.Run(() => communicationPolicy.SetSettings(settings, origin, revision,
                () => IsCallMutationHostEligible, value =>
                {
                    callAwarePreferences.Save(value);
                    return true;
                }, spokenApprovalDowngradeConfirmed));
            await CompleteAuditAsync(audit, outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied, outcome.ToString().ToLowerInvariant());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            outcome = CallMutationOutcome.PersistenceFailed;
            await CompleteAuditAsync(audit, SecurityAuditOutcome.Failed,
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
        !auditEvidenceUnavailable && Volatile.Read(ref pendingAuditWrites) == 0
        && !modelPreferenceWriteActive && !modelPreferencesUnavailable
        && IsCallMutationHostEligible
        && (sessionAllowedModelActions.Contains(action) || alwaysAllowedModelActions.Contains(action));

    private bool IsModelCallDispatchEligible(SecurityAuditInitiator initiator, long? observedCallRevision) =>
        initiator != SecurityAuditInitiator.ModelSuggestion
        || Volatile.Read(ref pendingAuditWrites) == 0
            && !modelPreferenceWriteActive && !modelPreferencesUnavailable && IsCallMutationHostEligible
            && observedCallRevision is not null;
}