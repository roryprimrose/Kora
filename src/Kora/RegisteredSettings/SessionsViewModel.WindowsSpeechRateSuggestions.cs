using Kora.Application.Configuration;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private WindowsSpeechRateSuggestionReview? windowsSpeechRateReview;
    private Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<WindowsSpeechRateSuggestionReview>>? reviewWindowsSpeechRateSuggestion;
    private Func<WindowsSpeechRateSuggestionReview, CancellationToken, Task<bool>>? confirmWindowsSpeechRateSuggestion;

    internal void BindWindowsSpeechRateSuggestions(
        Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<WindowsSpeechRateSuggestionReview>> review,
        Func<WindowsSpeechRateSuggestionReview, CancellationToken, Task<bool>> confirm)
    {
        if (closed || conversationBusy) { throw new InvalidOperationException("Cannot rebind rate review while closing or an operation is pending."); }
        CancelWindowsSpeechRateSuggestion();
        reviewWindowsSpeechRateSuggestion = review;
        confirmWindowsSpeechRateSuggestion = confirm;
        NotifyRegisteredSettings();
    }

    public bool CanReviewWindowsSpeechRateSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewWindowsSpeechRateSuggestion is not null && confirmWindowsSpeechRateSuggestion is not null;
    public bool CanConfirmWindowsSpeechRateSuggestion => CanReviewWindowsSpeechRateSuggestion && windowsSpeechRateReview?.Available == true;
    public string WindowsSpeechRateSuggestionDescription => windowsSpeechRateReview?.Description ?? string.Empty;

    public Task ConfirmWindowsSpeechRateSuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = windowsSpeechRateReview ?? throw new InvalidOperationException("Review an exact rate suggestion first.");
        windowsSpeechRateReview = null;
        try
        {
            var saved = await (confirmWindowsSpeechRateSuggestion
                ?? throw new InvalidOperationException("Rate confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = saved
                ? "Windows rate saved with committed preference and audit readback. Next eligible Windows speech only; Kokoro unchanged. No speech replayed."
                : UnconfirmedWindowsRateSave;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { throw new InvalidOperationException(UnconfirmedWindowsRateSave, exception); }
    });

    private const string UnconfirmedWindowsRateSave = "Windows rate save is unconfirmed. Inspect Settings and audit receipts; "
        + "a write may have persisted. No automatic retry or rollback.";

    private void CancelWindowsSpeechRateSuggestion()
    {
        windowsSpeechRateReview?.Dispose();
        windowsSpeechRateReview = null;
    }
}
