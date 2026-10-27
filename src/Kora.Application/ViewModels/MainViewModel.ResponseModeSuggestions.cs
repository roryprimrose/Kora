using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Diagnostics;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ResponseModeSuggestionReview? commandResponseModeReview;
    private Task responseModeSuggestionWork = Task.CompletedTask;

    public async Task<ResponseModeSuggestionReview> ReviewResponseModeSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var service = responseModeConfiguration
            ?? throw new InvalidOperationException("The response-mode configuration workflow is unavailable.");
        var time = (speechSuggestionBinding
            ?? throw new InvalidOperationException("The host retained-setting suggestion workflow is unavailable.")).Time;
        if (disposed || !responseModeSuggestionWork.IsCompleted || responseModeControlActive)
        { throw new InvalidOperationException("Response-mode review is unavailable while closing or another operation is pending."); }
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var name = AssistantName;
        bool Eligible() => !disposed && IsCallMutationHostEligible && !IsBusy && !IsResponseInteractionPending
            && !IsSpeechProviderOperationActive && privacyObservation.Current.TopologyRevision == privacy
            && CallPolicyRevision == call && settingsOwnershipRevision() == ownership
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Volatile.Read(ref voiceRecoveryRevision) == recovery
            && string.Equals(AssistantName, name, StringComparison.Ordinal)
            && (message.Request.Origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent)
            && sourceCurrent();
        var work = ResponseModeSuggestionReview.ReviewAsync(service, communicationPolicy, time, message, Eligible, validateSource, token);
        responseModeSuggestionWork = work;
        try { return await work.ConfigureAwait(false); }
        finally { responseModeSuggestionWork = Task.CompletedTask; }
    }

    public async Task<bool> ConfirmResponseModeSuggestionAsync(ResponseModeSuggestionReview review,
        CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (disposed || !responseModeSuggestionWork.IsCompleted || responseModeControlActive
            || responseModeConfiguration is null || !review.BelongsTo(responseModeConfiguration))
        { throw new InvalidOperationException("Response-mode confirmation is unavailable, foreign or already pending."); }
        var work = review.ConfirmAsync(token, initiator);
        responseModeSuggestionWork = work;
        try { return await work; }
        finally
        {
            responseModeSuggestionWork = Task.CompletedTask;
            SynchronizeResponseModeConfiguration();
        }
    }

    private void RetireResponseModeSuggestions()
    {
        commandResponseModeReview?.Dispose();
        commandResponseModeReview = null;
    }

    private async Task ExecuteResponseModeSuggestionAsync(RegisteredSettingSuggestionOperation operation,
        SecurityAuditInitiator initiator, bool prefixed)
    {
        if (IsResponseInteractionPending || initiator == SecurityAuditInitiator.VoiceCommand && !prefixed)
        {
            RetireSpeechSummarySuggestions();
            ShowInformation("Response-mode review denied.", "Finish pending decisions and use fresh original input with the current assistant-name prefix. Nothing was applied.");
            return;
        }
        var pinned = pinnedSpeechSuggestion;
        if (pinned is null)
        {
            ShowInformation("Response-mode source unavailable.", "Submit and complete one exact Active-session request first, or review its exact answer in Sessions. No latest-message guessing.");
            return;
        }
        var origin = initiator == SecurityAuditInitiator.VoiceCommand ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        var call = CallPolicyRevision;
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        bool Live() => ReferenceEquals(pinned, pinnedSpeechSuggestion) && pinned.Current()
            && IsCallMutationHostEligible && !IsResponseInteractionPending && CallPolicyRevision == call
            && Volatile.Read(ref voiceRecoveryRevision) == recovery
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent && CallObservation.AllowActivation);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), pinned.Target.Authority.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request);
        try
        {
            if (!Live()) { throw new InvalidOperationException("Original input admission changed. Start a new eligible request."); }
            if (operation == RegisteredSettingSuggestionOperation.Cancel)
            {
                RetireSpeechSummarySuggestions();
                ShowInformation("Response-mode review cancelled.", "No new response-mode write was requested.");
            }
            else if (operation == RegisteredSettingSuggestionOperation.Review)
            {
                RetireResponseModeSuggestions();
                async Task Validate(CancellationToken token)
                {
                    _ = await pinned.Binding.Conversation
                        .ReadSettingSuggestionAsync(pinned.Target, pinned.Message, Live, token).ConfigureAwait(false);
                }
                commandResponseModeReview = await ReviewResponseModeSuggestionAsync(pinned.Message, Live, Validate, CancellationToken.None);
                PresentResponse(AssistantState.Information, "Review exact response-mode suggestion.", commandResponseModeReview.Description,
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
            }
            else
            {
                using var review = commandResponseModeReview
                    ?? throw new InvalidOperationException("Review the exact response-mode suggestion first; confirmation is separate.");
                commandResponseModeReview = null;
                var saved = await ConfirmResponseModeSuggestionAsync(review, CancellationToken.None, initiator);
                RetireSpeechSummarySuggestions();
                PresentResponse(saved ? AssistantState.Information : AssistantState.Failure,
                    saved ? "Response mode saved." : "Response mode not confirmed.",
                    saved ? ResponseModeConfigurationStatus : "Response-mode save is unconfirmed. Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.",
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
                activity.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
                return;
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            RetireSpeechSummarySuggestions();
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            ApplicationLog.Error(logger, exception, "Reviewing an exact retained response-mode suggestion");
            if (!disposed && IsHostInputEligible)
            {
                ShowFailure("Response-mode change unconfirmed.", exception.Message
                    + " Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.");
            }
        }
    }
}
