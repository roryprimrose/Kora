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

    public Task ReviewAppearanceSuggestionAsync(ConversationMessage message) => RunConversationAsync(async (target, live) =>
    {
        CancelAppearanceSuggestion(reportCancellation: false);
        if (reviewAppearanceSuggestion is null || confirmAppearanceSuggestion is null
            || !target.Authority.IsActive || message.Generation != target.Authority.Generation || !access.CanControl
            || !ConversationMessages.Any(row => ReferenceEquals(row, message)))
        { throw new InvalidOperationException("Select an actual admitted answer from this active conversation's current page for appearance review."); }
        var page = conversationPage;
        var completed = completedConversationMessage;
        bool Current() => live() && access.CanControl && !conversationSourceHeld
            && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
        async Task Validate(CancellationToken token)
        {
            _ = await conversation!.ReadDetailAsync(target, message, Current, token).ConfigureAwait(false);
        }
        var review = await reviewAppearanceSuggestion(message, Current, Validate, lifetime.Token);
        if (!Current()) { review.Dispose(); throw new OperationCanceledException("Appearance suggestion source was revoked."); }
        appearanceReview = review;
        conversationStatus = "Review exact current and proposed shared appearance below. Nothing has changed; confirmation is separate.";
    });

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
                    : result.Error ?? "Appearance was not confirmed. Inspect settings before a fresh review.";
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
    }
}
