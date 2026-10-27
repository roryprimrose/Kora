using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private enum SettingReviewFamily { Appearance, Summary, Volume, ResponseMode }

    public string RegisteredSettingsDescription => "Supported inert suggestions: " + string.Join("; ",
        RegisteredSettingRegistry.Settings.Select(item => $"{item.Id}: "
            + item.SuggestedValues
            + (item.RequiresNativeReview ? " (native review only)" : string.Empty)))
        + ". All are shared device-local preferences. Review changes nothing.";

    public bool CanReviewRegisteredSettingSuggestion => CanReviewAppearanceSuggestion
        || CanReviewSpeechSummarySuggestion || CanReviewPlaybackVolumeSuggestion || CanReviewResponseModeSuggestion;
    public bool CanConfirmRegisteredSettingSuggestion => CanConfirmAppearanceSuggestion
        || CanConfirmSpeechSummarySuggestion || CanConfirmPlaybackVolumeSuggestion || CanConfirmResponseModeSuggestion;
    public string RegisteredSettingSuggestionDescription => AppearanceSuggestionDescription
        + SpeechSummarySuggestionDescription + PlaybackVolumeSuggestionDescription + ResponseModeSuggestionDescription;

    public Task ReviewRegisteredSettingSuggestionAsync(ConversationMessage message) =>
        ReviewRegisteredSettingSuggestionAsync(message, null);

    private Task ReviewRegisteredSettingSuggestionAsync(ConversationMessage message, SettingReviewFamily? expected) =>
        RunConversationAsync(async (target, live) =>
        {
            CancelRegisteredSettingSuggestion(reportCancellation: false);
            if (!access.CanControl || !target.Authority.IsActive || message.Generation != target.Authority.Generation
                || !ConversationMessages.Any(row => ReferenceEquals(row, message)))
            { throw new InvalidOperationException("Select an exact admitted answer from this Active conversation's current page for setting review."); }
            var proposal = RegisteredSettingSuggestions.Parse(message.AssistantResponse?.Body
                ?? throw new InvalidDataException("The selected message has no setting suggestion."));
            var family = proposal switch
            {
                RegisteredSettingProposal.Appearance => SettingReviewFamily.Appearance,
                RegisteredSettingProposal.Summary => SettingReviewFamily.Summary,
                RegisteredSettingProposal.Volume => SettingReviewFamily.Volume,
                RegisteredSettingProposal.ResponseMode => SettingReviewFamily.ResponseMode,
                _ => throw new InvalidDataException("The setting suggestion is not registered."),
            };
            if (expected is not null && family != expected)
            { throw new InvalidDataException("The exact suggestion belongs to a different setting family; nothing was applied."); }
            var page = conversationPage;
            var completed = completedConversationMessage;
            bool Current() => live() && access.CanControl && !conversationSourceHeld
                && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
            async Task Validate(CancellationToken token)
            {
                _ = await conversation!.ReadSettingSuggestionAsync(target, message, Current, token).ConfigureAwait(false);
            }
            switch (family)
            {
                case SettingReviewFamily.Appearance:
                    appearanceReview = await (reviewAppearanceSuggestion
                        ?? throw new InvalidOperationException("Native appearance review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.Summary:
                    speechSummaryReview = await (reviewSpeechSummarySuggestion
                        ?? throw new InvalidOperationException("Speech limit review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.Volume:
                    playbackVolumeReview = await (reviewPlaybackVolumeSuggestion
                        ?? throw new InvalidOperationException("Kora volume review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.ResponseMode:
                    responseModeReview = await (reviewResponseModeSuggestion
                        ?? throw new InvalidOperationException("Response-mode review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
            }
            if (!Current())
            {
                CancelRegisteredSettingSuggestion(reportCancellation: false);
                throw new OperationCanceledException("The exact setting suggestion source was revoked.");
            }
            conversationStatus = "Review exact current/proposed values and shared scope below. Nothing changed; confirmation is separate.";
        });

    public Task ConfirmRegisteredSettingSuggestionAsync() => appearanceReview is not null
        ? ConfirmAppearanceSuggestionAsync()
        : speechSummaryReview is not null ? ConfirmSpeechSummarySuggestionAsync()
        : responseModeReview is not null ? ConfirmResponseModeSuggestionAsync() : ConfirmPlaybackVolumeSuggestionAsync();

    public void CancelRegisteredSettingSuggestion(bool reportCancellation = true)
    {
        CancelAppearanceSuggestion(reportCancellation: false);
        CancelSpeechSummarySuggestion(reportCancellation: false);
        CancelPlaybackVolumeSuggestion(reportCancellation: false);
        CancelResponseModeSuggestion();
        if (reportCancellation)
        {
            conversationStatus = "Setting review cancelled. No new setting write was requested.";
            OnPropertyChanged(nameof(ConversationStatus));
        }
        NotifyRegisteredSettings();
    }

    private void NotifyRegisteredSettings()
    {
        if (responseModeReview is { Available: false }) { CancelResponseModeSuggestion(); }
        OnPropertyChanged(nameof(CanReviewRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(CanConfirmRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(RegisteredSettingSuggestionDescription));
    }
}
