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
    private InstalledSpeechVoiceSuggestionReview? commandInstalledVoiceReview;
    private Task installedVoiceReviewWork = Task.CompletedTask;
    private Task installedVoicePersistence = Task.CompletedTask;

    public async Task<InstalledSpeechVoiceSuggestionReview> ReviewInstalledSpeechVoiceSuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var sourceBinding = speechSuggestionBinding
            ?? throw new InvalidOperationException("The host retained-setting suggestion workflow is unavailable.");
        if (!installedVoicePersistence.IsCompleted || !installedVoiceReviewWork.IsCompleted)
        { throw new InvalidOperationException("Wait for the current installed voice operation before reviewing another suggestion."); }
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var name = AssistantName;
        bool Eligible() => !disposed && ReferenceEquals(sourceBinding, speechSuggestionBinding)
            && IsCallMutationHostEligible && !IsBusy && !IsResponseInteractionPending
            && !IsSpeechProviderOperationActive && privacyObservation.Current.TopologyRevision == privacy
            && CallPolicyRevision == call && settingsOwnershipRevision() == ownership
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Volatile.Read(ref voiceRecoveryRevision) == recovery
            && string.Equals(AssistantName, name, StringComparison.Ordinal)
            && (message.Request.Origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent)
            && sourceCurrent();
        var work = InstalledSpeechVoiceSuggestionReview.ReviewAsync(speechConfiguration, communicationPolicy, sourceBinding.Time,
            message, Eligible, validateSource, token);
        installedVoiceReviewWork = work;
        try { return await work; }
        finally { installedVoiceReviewWork = Task.CompletedTask; }
    }

    public async Task<SpeechApplyResult> ConfirmInstalledSpeechVoiceSuggestionAsync(InstalledSpeechVoiceSuggestionReview review,
        CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (disposed || !installedVoicePersistence.IsCompleted || !installedVoiceReviewWork.IsCompleted)
        { throw new InvalidOperationException("Installed voice confirmation is unavailable while closing or another operation is pending."); }
        var work = review.ConfirmAsync(token, initiator);
        installedVoicePersistence = work;
        try { return await work; }
        finally
        {
            installedVoicePersistence = Task.CompletedTask;
            SynchronizeSpeechConfiguration();
        }
    }

    private void RetireInstalledSpeechVoiceSuggestions()
    {
        commandInstalledVoiceReview?.Dispose();
        commandInstalledVoiceReview = null;
    }

    private async Task ExecuteInstalledSpeechVoiceSuggestionAsync(RegisteredSettingSuggestionOperation operation,
        SecurityAuditInitiator initiator, bool prefixed)
    {
        if (IsResponseInteractionPending || initiator == SecurityAuditInitiator.VoiceCommand && !prefixed)
        {
            RetireSpeechSummarySuggestions();
            ShowInformation("Installed voice review denied.", "Finish pending decisions and use fresh original input with the current assistant-name prefix. Nothing was applied.");
            return;
        }
        var pinned = pinnedSpeechSuggestion;
        if (pinned is null)
        {
            ShowInformation("Installed voice source unavailable.", "Submit and complete one exact Active-session request first, or review its exact answer in Sessions. No latest-message guessing.");
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
                ShowInformation("Installed voice review cancelled.", "No new speech selection write was requested.");
            }
            else if (operation == RegisteredSettingSuggestionOperation.Review)
            {
                RetireInstalledSpeechVoiceSuggestions();
                async Task Validate(CancellationToken token)
                {
                    _ = await pinned.Binding.Conversation
                        .ReadSettingSuggestionAsync(pinned.Target, pinned.Message, Live, token).ConfigureAwait(false);
                }
                commandInstalledVoiceReview = await ReviewInstalledSpeechVoiceSuggestionAsync(pinned.Message, Live, Validate, CancellationToken.None);
                PresentResponse(AssistantState.Information, "Review exact installed voice suggestion.", commandInstalledVoiceReview.Description,
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
            }
            else
            {
                using var review = commandInstalledVoiceReview
                    ?? throw new InvalidOperationException("Review the exact installed voice suggestion first; confirmation is separate.");
                commandInstalledVoiceReview = null;
                var result = await ConfirmInstalledSpeechVoiceSuggestionAsync(review, CancellationToken.None, initiator);
                RetireSpeechSummarySuggestions();
                PresentResponse(result.Succeeded ? AssistantState.Information : AssistantState.Failure,
                    result.Succeeded ? "Installed voice saved." : "Installed voice not confirmed.",
                    result.Succeeded
                        ? "Saved and read back the exact provider and voice pair. NEXT separately requested eligible ordinary speech only; no answer replayed."
                        : "Speech selection is unconfirmed. " + result.Error
                            + " Inspect Settings and audit receipts; bytes may have persisted. No automatic retry or rollback.",
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
                activity.Complete(result.Succeeded ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
                return;
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            RetireSpeechSummarySuggestions();
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            ApplicationLog.Error(logger, exception, "Reviewing an exact retained installed voice suggestion");
            if (!disposed && IsHostInputEligible)
            {
                ShowFailure("Installed voice change unconfirmed.", "Inspect the exact source, Settings and audit receipts before a fresh review. "
                    + "Bytes may have persisted. No automatic retry or rollback.");
            }
        }
    }
}
