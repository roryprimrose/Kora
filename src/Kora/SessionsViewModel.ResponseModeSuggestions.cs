using Kora.Application.Configuration;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ResponseModeSuggestionReview? responseModeReview;
    private Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<ResponseModeSuggestionReview>>? reviewResponseModeSuggestion;
    private Func<ResponseModeSuggestionReview, CancellationToken, Task<bool>>? confirmResponseModeSuggestion;

    internal void BindResponseModeSuggestions(
        Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<ResponseModeSuggestionReview>> review,
        Func<ResponseModeSuggestionReview, CancellationToken, Task<bool>> confirm)
    {
        if (closed || conversationBusy) { throw new InvalidOperationException("Cannot rebind response-mode review while closing or an operation is pending."); }
        CancelResponseModeSuggestion();
        reviewResponseModeSuggestion = review;
        confirmResponseModeSuggestion = confirm;
        NotifyRegisteredSettings();
    }

    public bool CanReviewResponseModeSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewResponseModeSuggestion is not null && confirmResponseModeSuggestion is not null;
    public bool CanConfirmResponseModeSuggestion => CanReviewResponseModeSuggestion && responseModeReview?.Available == true;
    public string ResponseModeSuggestionDescription => responseModeReview?.Description ?? string.Empty;

    public Task ConfirmResponseModeSuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = responseModeReview ?? throw new InvalidOperationException("Review an exact response-mode suggestion first.");
        responseModeReview = null;
        try
        {
            var saved = await (confirmResponseModeSuggestion
                ?? throw new InvalidOperationException("Response-mode confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = saved
                ? "Response mode saved with committed preference and audit readback. Future eligible output only; mandatory full visual, privacy and call exceptions remain. No speech replayed."
                : UnconfirmedResponseModeSave;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { throw new InvalidOperationException(UnconfirmedResponseModeSave, exception); }
    });

    private const string UnconfirmedResponseModeSave = "Response-mode save is unconfirmed. Inspect Settings and audit receipts; "
        + "a write may have persisted. No automatic retry or rollback.";

    private void CancelResponseModeSuggestion()
    {
        responseModeReview?.Dispose();
        responseModeReview = null;
    }
}
