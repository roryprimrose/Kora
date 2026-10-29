using Kora.Application.Communication;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private const string CallSpeechAuditAction = "speech.call-once";
    private CallSpeechReview? callSpeechReview;
    private CallSpeechLease? activeCallSpeechLease;
    private CancellationTokenRegistration callSpeechReviewExpiration;
    private bool callSpeechReviewOpening;
    private string callSpeechStatus = "No call speech exception is pending. Call protection is unchanged.";

    public AsyncCommand ReviewCallSpeechCommand { get; }
    public AsyncCommand ConfirmCallSpeechCommand { get; }
    public AsyncCommand CancelCallSpeechCommand { get; }
    public AsyncCommand GetCallSpeechStatusCommand { get; }
    public string CallSpeechStatus => callSpeechStatus;
    public bool CanReviewCallSpeech => currentResponsePresentation is not null
        && Volatile.Read(ref ordinaryResponse)?.Request is not null && IsProtectedCall && IsCallSpeechOtherwiseEligible
        && !callSpeechReviewOpening && !IsSpeaking && responseReadCancellation is null && activeCallSpeechLease is null;
    public bool CanConfirmCallSpeech => !callSpeechReviewOpening && callSpeechReview?.Available == true
        && !IsSpeaking && responseReadCancellation is null;

    private bool IsCallSpeechOtherwiseEligible => IsMandatorySpeechResponseEligible
        && ResponseOutputModeResolver.Resolve(DefaultResponseMode, QueueResponseMode, TaskResponseMode) != ResponseOutputMode.VisualOnly
        && !IsBusy && !IsSpeechProviderOperationActive && !speechInterruptionActive
        && CanEnterRetainedAnswerRead && !HasResponseActions;

    private bool IsResponseSpeechEligible(CallSpeechLease? lease) => lease is null
        ? IsSpeechResponseEnabled : IsCallSpeechOtherwiseEligible && lease.AllowsContinuation;

    private void ReportCallSpeech(string status)
    {
        callSpeechStatus = status;
        Transcript = status;
        NotifyCallSpeechChanged();
    }

    private void NotifyCallSpeechChanged()
    {
        OnPropertyChanged(nameof(CallSpeechStatus));
        OnPropertyChanged(nameof(CanReviewCallSpeech));
        OnPropertyChanged(nameof(CanConfirmCallSpeech));
        ReviewCallSpeechCommand.NotifyCanExecuteChanged();
        ConfirmCallSpeechCommand.NotifyCanExecuteChanged();
    }

    private void RetireCallSpeechReview()
    {
        var review = callSpeechReview;
        callSpeechReview = null;
        callSpeechReviewExpiration.Dispose();
        review?.Dispose();
        if (review is not null) { uiDispatcher.Post(() => CurrentResponseChanged?.Invoke(this, EventArgs.Empty)); }
    }

    private AsyncCommand CreateCallSpeechCommand(CallSpeechOperation operation, Func<bool>? canExecute = null) =>
        new(() => RunCallSpeechUiAsync(operation), exception =>
        {
            ResponseReadFailed(logger, exception.GetType().Name);
            ReportCallSpeech("Call speech completion or resource release is unconfirmed. Use Stop speaking and inspect recovery; no successful reading or replay is claimed.");
        }, canExecute);

    private Task RunCallSpeechUiAsync(CallSpeechOperation operation)
    {
        var original = HostActivity.Current;
        if (HostActivity.HasScope && (original is null
            || original.Outcome != HostOperationOutcome.Unknown || original.Request.InvocationId is not null))
        {
            ReportCallSpeech("Call speech unavailable: original input is retired or not a live user operation.");
            return Task.CompletedTask;
        }
        return Hosting.HostRequestRunner.RunAsync(original?.Request.Origin ?? RequestOrigin.LocalUi,
            () => ExecuteCallSpeechAsync(operation));
    }

    private async Task ExecuteCallSpeechAsync(CallSpeechOperation operation,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var original = HostActivity.Current;
        if (original is null || original.Activity!.IsStopped || original.Outcome != HostOperationOutcome.Unknown
            || original.Request.InvocationId is not null
            || original.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            ReportCallSpeech("Call speech unavailable: live original local user input is required.");
            return;
        }
        if (operation == CallSpeechOperation.Status)
        {
            if (activeCallSpeechLease is not null)
            { ReportCallSpeech("One reviewed response is in progress or awaiting resource release. No second exception is available."); }
            else if (callSpeechReview?.Available == true)
            { ReportCallSpeech("One exact response and destination are awaiting separate local confirmation. The review expires after two minutes; call protection is unchanged."); }
            else
            {
                RetireCallSpeechReview();
                ReportCallSpeech("No current call speech review is available. Initiate a fresh local review; no replay is queued. Call protection is unchanged.");
            }
            return;
        }
        if (operation == CallSpeechOperation.Cancel)
        {
            RetireCallSpeechReview();
            if (activeCallSpeechLease is { } playback)
            {
                RetireResponseReadPlayback();
                Exception? stopFailure = null;
                try { await textToSpeech.StopAsync(); }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
                {
                    playback.HoldUnconfirmedRelease();
                    stopFailure = exception;
                    ReportCallSpeech("Call speech cancellation requested; resource release is unconfirmed. Owned work is still joining; no successful cancellation is claimed.");
                }
#pragma warning disable VSTHRD003 // Cancellation retains the captured one-shot until its playback and terminal audit have joined.
                await responseReadQuiescence;
#pragma warning restore VSTHRD003
                if (textToSpeech.IsSpeaking || playback.ReleaseUnconfirmed)
                {
                    activeCallSpeechLease = playback;
                    playback.HoldUnconfirmedRelease();
                    IsSpeaking = true;
                    throw new InvalidOperationException("Call speech resource release is unconfirmed; use Stop speaking.", stopFailure);
                }
            }
            ReportCallSpeech("Call speech review cancelled; owned playback has joined. No exception or replay remains. Call protection is unchanged.");
            return;
        }
        if (original.Request.Origin != RequestOrigin.LocalUi
            || initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand))
        {
            ReportCallSpeech("Call speech exception denied: original voice or unknown input cannot initiate or confirm it, including a later native click. Start a fresh local UI review.");
            return;
        }
        var source = Volatile.Read(ref ordinaryResponse);
        var session = operation == CallSpeechOperation.Confirm ? callSpeechReview?.Request.SessionId : source?.Request?.SessionId;
        if (session is null)
        {
            ReportCallSpeech("Call speech unavailable: review one exact current ordinary response first. No fallback or replay is available.");
            return;
        }
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session.Value, new(Guid.NewGuid()), original.Request.Origin),
            HostActivityLayer.Application, HostOperation.Presentation, [new(original.Activity.Context)]);
        try
        {
            if (operation == CallSpeechOperation.Review) { await PrepareCallSpeechReviewAsync(initiator); }
            else
            {
                using var review = callSpeechReview!;
                callSpeechReview = null;
                await callSpeechReviewExpiration.DisposeAsync();
                using var lease = review.Confirm();
                CurrentResponseChanged?.Invoke(this, EventArgs.Empty);
                activeCallSpeechLease = lease;
                try
                {
                    ReportCallSpeech("Speaking only the separately confirmed response. Call protection remains active.");
                    await ReadResponseOnceAsync(lease.Target, initiator, lease, CancellationToken.None);
                    ReportCallSpeech(ReadCurrentResponseStatus);
                }
                finally
                {
                    if (!lease.ReleaseUnconfirmed) { activeCallSpeechLease = null; }
                }
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            RetireCallSpeechReview();
            ResponseReadFailed(logger, exception.GetType().Name);
            ReportCallSpeech("Call speech was not completed or confirmed. Inspect privacy, source, speech and output recovery, then make a fresh local review. No authority or replay is retained.");
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally { NotifyCallSpeechChanged(); }
    }

    private async Task PrepareCallSpeechReviewAsync(SecurityAuditInitiator initiator)
    {
        if (!CanReviewCallSpeech)
        { throw new InvalidOperationException("The exact current response, native review or ordinary speech gates are unavailable."); }
        RetireCallSpeechReview();
        callSpeechReviewOpening = true;
        var source = Volatile.Read(ref ordinaryResponse)!;
        var presenter = currentResponsePresentation!;
        var lifetime = presenter.CaptureLifetime();
        var presentation = Interlocked.Read(ref currentResponseBindingRevision);
        var privacy = privacyObservation.Current;
        var owner = auditImpactOwnerRevision();
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var question = Interlocked.Read(ref approvalPreparationRevision);
        var call = CallPolicyRevision;
        var speech = speechConfiguration.Get();
        var selected = SelectedOutputDevice;
        var output = EffectiveOutputDevice;
        var mode = EffectiveResponseMode;
        var ordinaryMode = ResponseOutputModeResolver.Resolve(DefaultResponseMode, QueueResponseMode, TaskResponseMode);
        var outputRevision = outputConfiguration?.Get(call).Revision;
        var responseModeRevision = responseModeConfiguration?.Get().Revision;
        var feedbackRevision = inCallFeedbackConfiguration?.Get().Revision;
        var volume = audioPlaybackSettings.VolumeState;
        var rate = audioPlaybackSettings.RateState;
        bool Eligible() => IsCallSpeechOtherwiseEligible && IsProtectedCall && lifetime()
            && ReferenceEquals(source, Volatile.Read(ref ordinaryResponse))
            && ReferenceEquals(presenter, currentResponsePresentation)
            && presentation == Interlocked.Read(ref currentResponseBindingRevision)
            && privacyObservation.Current == privacy && owner == auditImpactOwnerRevision()
            && ownerBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && recovery == Interlocked.Read(ref voiceRecoveryRevision)
            && input == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && question == Interlocked.Read(ref approvalPreparationRevision)
            && call == CallPolicyRevision && speech == speechConfiguration.Get()
            && selected == SelectedOutputDevice && output == EffectiveOutputDevice && mode == EffectiveResponseMode
            && ordinaryMode == ResponseOutputModeResolver.Resolve(DefaultResponseMode, QueueResponseMode, TaskResponseMode)
            && outputRevision == outputConfiguration?.Get(call).Revision
            && responseModeRevision == responseModeConfiguration?.Get().Revision
            && feedbackRevision == inCallFeedbackConfiguration?.Get().Revision
            && volume == audioPlaybackSettings.VolumeState && rate == audioPlaybackSettings.RateState;
        SecurityAuditEvent? audit = null;
        CallSpeechReview? review = null;
        try
        {
            audit = await StartAuditAsync(SecurityAuditCategory.ProtectedOperation, "speech.call-once.review", initiator,
                source.Id.ToString("D"));
            if (!Eligible() || !await RefreshOutputEndpointsAsync(preserveResponse: true) || !Eligible())
            { throw new InvalidOperationException("Call speech source or destination changed before review."); }
            var text = $"{source.Title}. {source.Body}";
            if (speech.SummaryLimits is not { } limits || !SpokenSummaryMeasure.Count(text).Fits(limits))
            { throw new InvalidOperationException("The exact current response exceeds ordinary speech limits or limits are unavailable."); }
            review = new(communicationPolicy, speechSuggestionBinding?.Time ?? TimeProvider.System,
                new(source.Id, source.Revision, call), text, output! with { IsSystemDefault = false }, Eligible);
            var description = $"Speak ONE current response during call protection. Output: {output!.Name}; "
                + $"voice: {speech.EffectiveVoice!.Name} ({speech.EffectiveVoice.ProviderId}); "
                + $"Kora volume: {(volume is null ? "existing engine setting" : volume.Effective!.Value.Percent.ToString(System.Globalization.CultureInfo.InvariantCulture))}; "
                + $"Windows rate: {rate?.Effective?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "existing engine setting"}. "
                + $"Ordinary mode: {ordinaryMode}; call feedback: {DesiredInCallFeedback}. "
                + "Only the exact text below is eligible. Review it and separately Confirm speak once, or Cancel. "
                + "Expires in two minutes; changes retire it. Lock, privacy, mute, capture, consent and speech availability still win. "
                + "No setting, approval, backlog or reusable authority changes.";
            var request = source.Request!;
            var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), source.Revision), DetailContentKind.PlainText,
                DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
                "Review one call speech exception", "Exact host-owned single-response review; no settings change.",
                description + "\n\nEXACT RESPONSE TO SPEAK (nothing else):\n\n" + text,
                new(request.SessionId, request.RequestId, request.TaskId));
            var acknowledged = presenter.OpenDetails(content, () => review.Available) && review.Available;
            await WriteAuditAsync(audit.WithOutcome(acknowledged ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Cancelled,
                acknowledged ? "exact-native-review-opened" : "native-review-unavailable"));
            audit = null;
            if (!acknowledged || !review.Available)
            { throw new InvalidOperationException("The complete exact native review was not acknowledged or has retired."); }
            callSpeechReview = review;
            var pending = review;
            var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation);
            callSpeechReviewExpiration = review.Expiration.Register(() => uiDispatcher.Post(() =>
            {
                using var expiry = continuation();
                if (ReferenceEquals(callSpeechReview, pending))
                {
                    RetireCallSpeechReview();
                    ReportCallSpeech("Call speech review expired. Make a fresh local review; no exception or replay remains.");
                }
                expiry.Complete(HostOperationOutcome.Cancelled);
            }));
            ReportCallSpeech("Exact response and destination opened for review. Separately Confirm speak once within two minutes, or Cancel. Nothing has been played or changed.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            if (audit is not null) { await WriteAuditAsync(audit.WithOutcome(SecurityAuditOutcome.Failed, "review-unconfirmed")); }
            throw;
        }
        finally
        {
            if (review is not null && !ReferenceEquals(callSpeechReview, review)) { review.Dispose(); }
            callSpeechReviewOpening = false;
        }
    }
}
