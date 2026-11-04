using Kora.Application.Configuration;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private VoiceCaptureTimingSuggestionReview? captureTimingSuggestionReview;
    private volatile object? captureTimingSuggestionIdentity;
    private Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<VoiceCaptureTimingSuggestionReview>>? reviewCaptureTimingSuggestion;
    private Func<VoiceCaptureTimingSuggestionReview, CancellationToken, Task<bool>>? confirmCaptureTimingSuggestion;

    internal void BindVoiceCaptureTimingSuggestions(
        Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<VoiceCaptureTimingSuggestionReview>> review,
        Func<VoiceCaptureTimingSuggestionReview, CancellationToken, Task<bool>> confirm)
    {
        if (closed || conversationBusy) { throw new InvalidOperationException("Cannot rebind capture timing review during an operation or close."); }
        CancelCaptureTimingSuggestion();
        reviewCaptureTimingSuggestion = review;
        confirmCaptureTimingSuggestion = confirm;
        NotifyRegisteredSettings();
    }

    public bool CanReviewCaptureTimingSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewCaptureTimingSuggestion is not null && confirmCaptureTimingSuggestion is not null;
    public bool CanConfirmCaptureTimingSuggestion => CanReviewCaptureTimingSuggestion && captureTimingSuggestionReview?.Available == true;
    public string CaptureTimingSuggestionDescription => captureTimingSuggestionReview?.Description ?? string.Empty;

    public Task ConfirmCaptureTimingSuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = captureTimingSuggestionReview ?? throw new InvalidOperationException("Review an exact capture timing suggestion first.");
        try
        {
            var saved = await (confirmCaptureTimingSuggestion
                ?? throw new InvalidOperationException("Capture timing confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = saved
                ? "Capture timing saved with committed preference and audit readback. Future eligible captures only; current capture unchanged. No microphone opened."
                : UnconfirmedCaptureTimingSave;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        { throw new InvalidOperationException(UnconfirmedCaptureTimingSave, exception); }
        finally { CancelCaptureTimingSuggestion(); }
    }, requireOriginalInput: true);

    private const string UnconfirmedCaptureTimingSave = "Capture timing save is unconfirmed. Inspect Settings and audit receipts; "
        + "a write may have persisted. No automatic retry or rollback.";

    private void CancelCaptureTimingSuggestion()
    {
        captureTimingSuggestionIdentity = null;
        captureTimingSuggestionReview?.Dispose();
        captureTimingSuggestionReview = null;
    }
}
