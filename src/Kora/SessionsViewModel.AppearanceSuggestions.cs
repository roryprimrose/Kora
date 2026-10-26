using Kora.Application.Configuration;
using Kora.Application.Presentation;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private AppearanceSuggestionReview? appearanceReview;
    public bool CanReviewAppearanceSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewAppearanceSuggestion is not null && confirmAppearanceSuggestion is not null;
    public bool CanConfirmAppearanceSuggestion => CanReviewAppearanceSuggestion && appearanceReview?.Available == true;
    public string AppearanceSuggestionDescription => appearanceReview?.Description ?? string.Empty;

    public Task ReviewAppearanceSuggestionAsync(ConversationMessage message) =>
        ReviewRegisteredSettingSuggestionAsync(message, SettingReviewFamily.Appearance);

    public Task ConfirmAppearanceSuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        var review = appearanceReview ?? throw new InvalidOperationException("Review an exact appearance suggestion first.");
        appearanceReview = null;
        using (review)
        {
            try
            {
                var result = await (confirmAppearanceSuggestion
                    ?? throw new InvalidOperationException("Native appearance confirmation is unavailable."))(review, lifetime.Token);
                conversationStatus = result.Succeeded ? SettingsText.Describe(result.State)
                    : "Appearance save is unconfirmed. " + result.Error
                        + " Inspect Settings and audit receipts; a write may have persisted. No automatic retry or rollback.";
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException
                or UnauthorizedAccessException or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "Appearance save is unconfirmed. Inspect Settings and audit receipts before a fresh review; "
                    + "a write may have persisted. No automatic retry or rollback.", exception);
            }
        }
    });

    public void CancelAppearanceSuggestion(bool reportCancellation = true)
    {
        appearanceReview?.Dispose();
        appearanceReview = null;
        if (reportCancellation)
        {
            conversationStatus = "Appearance review cancelled. No new appearance change was requested.";
            OnPropertyChanged(nameof(ConversationStatus));
        }
        OnPropertyChanged(nameof(AppearanceSuggestionDescription));
        OnPropertyChanged(nameof(CanConfirmAppearanceSuggestion));
        NotifyRegisteredSettings();
    }
}
