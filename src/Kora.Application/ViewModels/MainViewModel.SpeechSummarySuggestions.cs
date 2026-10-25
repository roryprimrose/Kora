using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SpeechSuggestionBinding? speechSuggestionBinding;
    private PinnedSpeechSuggestion? pinnedSpeechSuggestion;
    private SpeechSummarySuggestionReview? commandSpeechReview;
    private Task speechSummaryPersistence = Task.CompletedTask;

    private sealed record SpeechSuggestionBinding(SessionConversationService Conversation, TimeProvider Time);
    private sealed record PinnedSpeechSuggestion(SessionWorkspaceEntry Target, ConversationMessage Message,
        Func<bool> Current, SpeechSuggestionBinding Binding);

    public void BindSpeechSummarySuggestions(SessionConversationService conversation, TimeProvider time)
    {
        RetireSpeechSummarySuggestions();
        speechSuggestionBinding = new(conversation, time);
    }

    public Task<SpeechSummarySuggestionReview> ReviewSpeechSummarySuggestionAsync(ConversationMessage message,
        Func<bool> sourceCurrent, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var time = (speechSuggestionBinding
            ?? throw new InvalidOperationException("The host speech suggestion workflow is unavailable.")).Time;
        if (!speechSummaryPersistence.IsCompleted)
        { throw new InvalidOperationException("Wait for the current speech limit confirmation outcome before reviewing another suggestion."); }
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
        return SpeechSummarySuggestionReview.ReviewAsync(speechConfiguration, communicationPolicy, time,
            message, Eligible, validateSource, token);
    }

    public async Task<SpeechApplyResult> ConfirmSpeechSummarySuggestionAsync(SpeechSummarySuggestionReview review,
        CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        if (disposed || !speechSummaryPersistence.IsCompleted)
        { throw new InvalidOperationException("Speech limit confirmation is unavailable while closing or another save is pending."); }
        var work = review.ConfirmAsync(token, initiator);
        speechSummaryPersistence = work;
        try { return await work; }
        finally
        {
            speechSummaryPersistence = Task.CompletedTask;
            SynchronizeSpeechConfiguration();
        }
    }

    private async Task PinSpeechSummarySuggestionAsync(ConversationSubmissionReceipt receipt, Func<bool> sourceCurrent,
        SessionWorkspaceService workspace)
    {
        RetireSpeechSummarySuggestions();
        var message = receipt.Message;
        message.Validate();
        if (message.AssistantResponse is null) { return; }
        var binding = speechSuggestionBinding;
        if (binding is null) { return; }
        var target = await workspace.ReadExactMetadataAsync(message.Request.SessionId, CancellationToken.None);
        if (!sourceCurrent() || target.Authority.Generation != message.Generation || !target.Authority.IsActive
            || (target.Metadata?.Revision.Value ?? 0) != message.ExecutionMetadataRevision
            || !ReferenceEquals(binding, speechSuggestionBinding))
        { throw new InvalidOperationException("The exact successful submission receipt is no longer current."); }
        pinnedSpeechSuggestion = new(target, message, sourceCurrent, binding);
    }

    private void RetireSpeechSummarySuggestions()
    {
        RetirePlaybackVolumeSuggestions();
        commandSpeechReview?.Dispose();
        commandSpeechReview = null;
        pinnedSpeechSuggestion = null;
    }

    private async Task ExecuteSpeechSummarySuggestionAsync(SpeechSummarySuggestionOperation operation,
        SecurityAuditInitiator initiator, bool prefixed)
    {
        if (IsResponseInteractionPending)
        {
            RetireSpeechSummarySuggestions();
            ShowInformation("Speech limit review blocked.", "Finish or cancel the pending question or approval first. No decision was supplied.");
            return;
        }
        if (initiator == SecurityAuditInitiator.VoiceCommand && !prefixed)
        {
            RetireSpeechSummarySuggestions();
            ShowInformation("Speech limit review denied.", "Use the current assistant-name prefix and a fresh exact source. Nothing was applied.");
            return;
        }
        var pinned = pinnedSpeechSuggestion;
        if (pinned is null)
        {
            ShowInformation("Speech limit source unavailable.", "Submit and complete one exact Active-session request first, or review its exact answer in Sessions. No latest-message guessing.");
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
            if (operation == SpeechSummarySuggestionOperation.Cancel)
            {
                RetireSpeechSummarySuggestions();
                ShowInformation("Speech limit review cancelled.", "No new speech limit write was requested.");
            }
            else if (operation == SpeechSummarySuggestionOperation.Review)
            {
                commandSpeechReview?.Dispose();
                commandSpeechReview = null;
                async Task Validate(CancellationToken token)
                {
                    _ = await pinned.Binding.Conversation
                        .ReadSpeechSummarySuggestionAsync(pinned.Target, pinned.Message, Live, token).ConfigureAwait(false);
                }
                commandSpeechReview = await ReviewSpeechSummarySuggestionAsync(pinned.Message, Live, Validate, CancellationToken.None);
                PresentResponse(AssistantState.Information, "Review exact speech limit suggestion.", commandSpeechReview.Description,
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
            }
            else
            {
                using var review = commandSpeechReview
                    ?? throw new InvalidOperationException("Review the exact speech limit suggestion first; confirmation is separate.");
                commandSpeechReview = null;
                var result = await ConfirmSpeechSummarySuggestionAsync(review, CancellationToken.None, initiator);
                RetireSpeechSummarySuggestions();
                if (result.Succeeded)
                {
                    PresentResponse(AssistantState.Information, "Speech limit saved.", DescribeSpeechConfiguration(result.State),
                        refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
                }
                else
                {
                    PresentResponse(AssistantState.Failure, "Speech limit not confirmed.", result.Error!,
                        refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
                }
                activity.Complete(result.Succeeded ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
                return;
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            RetireSpeechSummarySuggestions();
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            ApplicationLog.Error(logger, exception, "Reviewing an exact retained speech limit suggestion");
            if (!disposed && IsHostInputEligible)
            {
                ShowFailure("Speech limit change unconfirmed.", exception.Message
                    + " Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.");
            }
        }
    }
}
