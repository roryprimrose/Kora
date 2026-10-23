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

    public Task ReviewSpeechSummarySuggestionAsync(ConversationMessage message) => RunConversationAsync(async (target, live) =>
    {
        CancelSpeechSummarySuggestion(reportCancellation: false);
        if (reviewSpeechSummarySuggestion is null || !access.CanControl || !target.Authority.IsActive
            || message.Generation != target.Authority.Generation || !ConversationMessages.Any(row => ReferenceEquals(row, message)))
        { throw new InvalidOperationException("Select an exact retained answer from this Active conversation for speech limit review."); }
        var page = conversationPage;
        var completed = completedConversationMessage;
        bool Current() => live() && access.CanControl && !conversationSourceHeld
            && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
        async Task Validate(CancellationToken token)
        {
            _ = await conversation!.ReadSpeechSummarySuggestionAsync(target, message, Current, token).ConfigureAwait(false);
        }
        var review = await reviewSpeechSummarySuggestion(message, Current, Validate, lifetime.Token);
        if (!Current()) { review.Dispose(); throw new OperationCanceledException("Speech suggestion source revoked."); }
        speechSummaryReview = review;
        conversationStatus = "Review the complete current/proposed shared speech limits below. Nothing changed; confirmation is separate.";
    });

    public Task ConfirmSpeechSummarySuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = speechSummaryReview ?? throw new InvalidOperationException("Review an exact speech limit suggestion first.");
        speechSummaryReview = null;
        try
        {
            var result = await (confirmSpeechSummarySuggestion
                ?? throw new InvalidOperationException("Speech limit confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = result.Succeeded ? SettingsText.Describe(result.State)
                : result.Error ?? "Speech limit not confirmed. Inspect Settings and audit receipts before a fresh review.";
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
    }
}
