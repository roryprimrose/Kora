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
    private WindowsSpeechRateSuggestionReview? commandRateReview;
    private Task rateSuggestionReviewWork = Task.CompletedTask;
    private Task rateSuggestionPersistence = Task.CompletedTask;

    public async Task<WindowsSpeechRateSuggestionReview> ReviewWindowsSpeechRateSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var time = (speechSuggestionBinding
            ?? throw new InvalidOperationException("The host retained-setting suggestion workflow is unavailable.")).Time;
        if (!rateSuggestionPersistence.IsCompleted || !rateSuggestionReviewWork.IsCompleted || rateControlActive)
        { throw new InvalidOperationException("Wait for the current rate operation before reviewing another suggestion."); }
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
        var work = audioPlaybackSettings.ReviewRateSuggestionAsync(communicationPolicy, time,
            message, Eligible, validateSource, token);
        rateSuggestionReviewWork = work;
        try { return await work; }
        finally { rateSuggestionReviewWork = Task.CompletedTask; }
    }

    public async Task<bool> ConfirmWindowsSpeechRateSuggestionAsync(WindowsSpeechRateSuggestionReview review,
        CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (disposed || !rateSuggestionPersistence.IsCompleted || !rateSuggestionReviewWork.IsCompleted || rateControlActive)
        { throw new InvalidOperationException("Rate confirmation is unavailable while closing or another save is pending."); }
        var work = audioPlaybackSettings.ConfirmRateSuggestionAsync(review, initiator, token);
        rateSuggestionPersistence = work;
        try { return await work; }
        finally
        {
            rateSuggestionPersistence = Task.CompletedTask;
            SynchronizeWindowsSpeechRate();
        }
    }

    private void RetireWindowsSpeechRateSuggestions()
    {
        commandRateReview?.Dispose();
        commandRateReview = null;
    }

    private async Task ExecuteWindowsSpeechRateSuggestionAsync(RegisteredSettingSuggestionOperation operation,
        SecurityAuditInitiator initiator, bool prefixed)
    {
        if (IsResponseInteractionPending || initiator == SecurityAuditInitiator.VoiceCommand && !prefixed)
        {
            RetireSpeechSummarySuggestions();
            ShowInformation("Rate review denied.", "Finish pending decisions and use fresh original input with the current assistant-name prefix. Nothing was applied.");
            return;
        }
        var pinned = pinnedSpeechSuggestion;
        if (pinned is null)
        {
            ShowInformation("Rate source unavailable.", "Submit and complete one exact Active-session request first, or review its exact answer in Sessions. No latest-message guessing.");
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
                ShowInformation("Rate review cancelled.", "No new rate write was requested.");
            }
            else if (operation == RegisteredSettingSuggestionOperation.Review)
            {
                RetireWindowsSpeechRateSuggestions();
                async Task Validate(CancellationToken token)
                {
                    _ = await pinned.Binding.Conversation
                        .ReadSettingSuggestionAsync(pinned.Target, pinned.Message, Live, token).ConfigureAwait(false);
                }
                commandRateReview = await ReviewWindowsSpeechRateSuggestionAsync(pinned.Message, Live, Validate, CancellationToken.None);
                PresentResponse(AssistantState.Information, "Review exact Windows rate suggestion.", commandRateReview.Description,
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
            }
            else
            {
                using var review = commandRateReview
                    ?? throw new InvalidOperationException("Review the exact rate suggestion first; confirmation is separate.");
                commandRateReview = null;
                var saved = await ConfirmWindowsSpeechRateSuggestionAsync(review, CancellationToken.None, initiator);
                RetireSpeechSummarySuggestions();
                PresentResponse(saved ? AssistantState.Information : AssistantState.Failure,
                    saved ? "Windows rate saved." : "Windows rate not confirmed.",
                    saved ? audioPlaybackSettings.WindowsSpeechRateStatus : "Rate was denied. Inspect Settings and audit receipts before a fresh review.",
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
            ApplicationLog.Error(logger, exception, "Reviewing an exact retained Windows rate suggestion");
            if (!disposed && IsHostInputEligible)
            {
                ShowFailure("Rate change unconfirmed.", exception.Message
                    + " Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.");
            }
        }
    }
}
