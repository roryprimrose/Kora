using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Diagnostics;
using Kora.Application.Presentation;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private AppearanceSuggestionReview? commandResponseTimeoutReview;

    private void RetireResponseTimeoutSuggestions()
    {
        commandResponseTimeoutReview?.Dispose();
        commandResponseTimeoutReview = null;
    }

    private async Task ExecuteResponseTimeoutSuggestionAsync(RegisteredSettingSuggestionOperation operation,
        SecurityAuditInitiator initiator, bool prefixed)
    {
        if (IsResponseInteractionPending || initiator == SecurityAuditInitiator.VoiceCommand && !prefixed)
        {
            RetireSpeechSummarySuggestions();
            ShowInformation("Response-timeout review denied.",
                "Finish pending decisions and use fresh original input with the current assistant-name prefix. Nothing was applied.");
            return;
        }
        var pinned = pinnedSpeechSuggestion;
        if (pinned is null)
        {
            ShowInformation("Response-timeout source unavailable.",
                "Submit and complete one exact Active-session request first, or review its exact answer in Sessions. No latest-message guessing.");
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
                ShowInformation("Response-timeout review cancelled.", "No new response-timeout write was requested.");
            }
            else if (operation == RegisteredSettingSuggestionOperation.Review)
            {
                RetireResponseTimeoutSuggestions();
                if (RegisteredSettingSuggestions.Parse(pinned.Message.AssistantResponse!.Body)
                    is not RegisteredSettingProposal.Appearance { Option: AppearanceOption.ResponseTimeout })
                { throw new InvalidDataException("Request one exact appearance.response-timeout Set suggestion only."); }
                async Task Validate(CancellationToken token)
                {
                    _ = await pinned.Binding.Conversation
                        .ReadSettingSuggestionAsync(pinned.Target, pinned.Message, Live, token).ConfigureAwait(false);
                }
                commandResponseTimeoutReview = await ReviewAppearanceSuggestionAsync(pinned.Message, Live, Validate, CancellationToken.None);
                PresentResponse(AssistantState.Information, "Review exact response-timeout suggestion.", commandResponseTimeoutReview.Description,
                    refreshOutput: false, sourceAssigned: () => forceVisualResponse = true);
            }
            else
            {
                using var review = commandResponseTimeoutReview
                    ?? throw new InvalidOperationException("Review the exact response-timeout suggestion first; confirmation is separate.");
                commandResponseTimeoutReview = null;
                var result = await ConfirmAppearanceSuggestionAsync(review, initiator, CancellationToken.None);
                RetireSpeechSummarySuggestions();
                PresentResponse(result.Succeeded ? AssistantState.Information : AssistantState.Failure,
                    result.Succeeded ? "Response timeout saved." : "Response timeout not confirmed.",
                    result.Succeeded ? SettingsText.Describe(result.State)
                        : "Response-timeout save is unconfirmed. Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.",
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
            ApplicationLog.Error(logger, exception, "Reviewing an exact retained response-timeout suggestion");
            if (!disposed && IsHostInputEligible)
            {
                ShowFailure("Response-timeout change unconfirmed.", exception.Message
                    + " Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.");
            }
        }
    }
}
