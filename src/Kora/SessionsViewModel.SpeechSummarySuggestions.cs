using Kora.Application.Configuration;
using Kora.Application.Presentation;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SpeechSummarySuggestionReview? speechSummaryReview;
    public bool CanReviewSpeechSummarySuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewSpeechSummarySuggestion is not null && confirmSpeechSummarySuggestion is not null;
    public bool CanConfirmSpeechSummarySuggestion => CanReviewSpeechSummarySuggestion && speechSummaryReview?.Available == true;
    public string SpeechSummarySuggestionDescription => speechSummaryReview?.Description ?? string.Empty;

    public Task ReviewSpeechSummarySuggestionAsync(ConversationMessage message) =>
        ReviewRegisteredSettingSuggestionAsync(message, SettingReviewFamily.Summary);

    public Task ConfirmSpeechSummarySuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = speechSummaryReview ?? throw new InvalidOperationException("Review an exact speech limit suggestion first.");
        speechSummaryReview = null;
        try
        {
            var result = await (confirmSpeechSummarySuggestion
                ?? throw new InvalidOperationException("Speech limit confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = result.Succeeded ? SettingsText.Describe(result.State)
                : "Speech limit save is unconfirmed. " + result.Error
                    + " Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException("Speech limit save is unconfirmed. Inspect Settings and audit receipts; "
                + "a write may have persisted. No automatic retry or rollback.", exception);
        }
    });

    public void CancelSpeechSummarySuggestion(bool reportCancellation = true)
    {
        speechSummaryReview?.Dispose();
        speechSummaryReview = null;
        if (reportCancellation)
        {
            conversationStatus = "Speech limit review cancelled. No new speech limit write was requested.";
            OnPropertyChanged(nameof(ConversationStatus));
        }
        OnPropertyChanged(nameof(SpeechSummarySuggestionDescription));
        OnPropertyChanged(nameof(CanConfirmSpeechSummarySuggestion));
        NotifyRegisteredSettings();
    }
}
