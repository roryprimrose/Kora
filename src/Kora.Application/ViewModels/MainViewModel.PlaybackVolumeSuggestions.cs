using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Presentation;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private PlaybackVolumeSuggestionReview? commandVolumeReview;
    private Task volumeSuggestionReviewWork = Task.CompletedTask;
    private Task volumeSuggestionPersistence = Task.CompletedTask;

    public async Task<PlaybackVolumeSuggestionReview> ReviewPlaybackVolumeSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var time = (speechSuggestionBinding
            ?? throw new InvalidOperationException("The host retained-setting suggestion workflow is unavailable.")).Time;
        if (!volumeSuggestionPersistence.IsCompleted || !volumeSuggestionReviewWork.IsCompleted || volumeControlActive)
        { throw new InvalidOperationException("Wait for the current volume operation before reviewing another suggestion."); }
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
        var work = audioPlaybackSettings.ReviewVolumeSuggestionAsync(communicationPolicy, time,
            message, Eligible, validateSource, token);
        volumeSuggestionReviewWork = work;
        try { return await work; }
        finally { volumeSuggestionReviewWork = Task.CompletedTask; }
    }

    public async Task<bool> ConfirmPlaybackVolumeSuggestionAsync(PlaybackVolumeSuggestionReview review,
        CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (disposed || !volumeSuggestionPersistence.IsCompleted || !volumeSuggestionReviewWork.IsCompleted || volumeControlActive)
        { throw new InvalidOperationException("Volume confirmation is unavailable while closing or another save is pending."); }
        var work = audioPlaybackSettings.ConfirmVolumeSuggestionAsync(review, initiator, token);
        volumeSuggestionPersistence = work;
        try { return await work; }
        finally
        {
            volumeSuggestionPersistence = Task.CompletedTask;
            SynchronizePlaybackVolume();
        }
    }

    private void RetirePlaybackVolumeSuggestions()
    {
        commandVolumeReview?.Dispose();
        commandVolumeReview = null;
    }

    private async Task ExecutePlaybackVolumeSuggestionAsync(PlaybackVolumeSuggestionOperation operation,
        SecurityAuditInitiator initiator, bool prefixed)
    {
        if (IsResponseInteractionPending || initiator == SecurityAuditInitiator.VoiceCommand && !prefixed)
        {
            RetireSpeechSummarySuggestions();
            ShowInformation("Volume review denied.", "Finish pending decisions and use fresh original input with the current assistant-name prefix. Nothing was applied.");
            return;
        }
        // The shared successful-submission pin is never a latest-answer lookup.
        var pinned = pinnedSpeechSuggestion;
        if (pinned is null)
        {
            ShowInformation("Volume source unavailable.", "Submit and complete one exact Active-session request first, or review its exact answer in Sessions. No latest-message guessing.");
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
            if (operation == PlaybackVolumeSuggestionOperation.Cancel)
            {
                RetireSpeechSummarySuggestions();
                ShowInformation("Volume review cancelled.", "No new volume write was requested.");
            }
            else if (operation == PlaybackVolumeSuggestionOperation.Review)
            {
                RetirePlaybackVolumeSuggestions();
                async Task Validate(CancellationToken token)
                {
                    _ = await pinned.Binding.Conversation
                        .ReadSettingSuggestionAsync(pinned.Target, pinned.Message, Live, token).ConfigureAwait(false);
                }
                commandVolumeReview = await ReviewPlaybackVolumeSuggestionAsync(pinned.Message, Live, Validate, CancellationToken.None);
                PresentResponse(AssistantState.Information, "Review exact volume suggestion.", commandVolumeReview.Description,
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
            }
            else
            {
                using var review = commandVolumeReview
                    ?? throw new InvalidOperationException("Review the exact volume suggestion first; confirmation is separate.");
                commandVolumeReview = null;
                var saved = await ConfirmPlaybackVolumeSuggestionAsync(review, CancellationToken.None, initiator);
                RetireSpeechSummarySuggestions();
                PresentResponse(saved ? AssistantState.Information : AssistantState.Failure,
                    saved ? "Kora volume saved." : "Kora volume not confirmed.",
                    saved ? audioPlaybackSettings.PlaybackVolumeStatus : "Volume was denied. Inspect Settings and audit receipts before a fresh review.",
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
            ApplicationLog.Error(logger, exception, "Reviewing an exact retained playback volume suggestion");
            if (!disposed && IsHostInputEligible)
            {
                ShowFailure("Volume change unconfirmed.", exception.Message
                    + " Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.");
            }
        }
    }
}
