using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private QuietHoursSuggestionReview? quietHoursSuggestionReview;
    private volatile object? quietHoursSuggestionIdentity;
    private Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<QuietHoursSuggestionReview>>? reviewQuietHoursSuggestion;
    private Func<QuietHoursSuggestionReview, CancellationToken, Task<bool>>? confirmQuietHoursSuggestion;
    internal Func<Func<bool>>? CaptureQuietHoursNativeLifetime { get; set; }

    internal void BindQuietHoursSuggestions(
        Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<QuietHoursSuggestionReview>> review,
        Func<QuietHoursSuggestionReview, CancellationToken, Task<bool>> confirm)
    {
        if (closed || conversationBusy) { throw new InvalidOperationException("Cannot rebind quiet-hours review during an operation or closure."); }
        CancelQuietHoursSuggestion();
        reviewQuietHoursSuggestion = review;
        confirmQuietHoursSuggestion = confirm;
        NotifyRegisteredSettings();
    }

    private bool CanReviewQuietHoursSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewQuietHoursSuggestion is not null && confirmQuietHoursSuggestion is not null;
    private bool CanConfirmQuietHoursSuggestion => CanReviewQuietHoursSuggestion && quietHoursSuggestionReview?.Available == true;
    private string QuietHoursSuggestionDescription => quietHoursSuggestionReview?.Description ?? string.Empty;

    private Task ConfirmQuietHoursSuggestionAsync()
    {
        var originalInput = HostActivity.Current;
        return RunConversationAsync(async (_, _) =>
        {
            RequireQuietHoursNativeInput(originalInput);
            using var review = quietHoursSuggestionReview ?? throw new InvalidOperationException("Review the exact quiet-hours answer first.");
            quietHoursSuggestionReview = null;
            try
            {
                var saved = await (confirmQuietHoursSuggestion
                    ?? throw new InvalidOperationException("Quiet-hours confirmation is unavailable."))(review, lifetime.Token);
                conversationStatus = saved
                    ? "Quiet hours saved with committed preference and audit readback. Future already-consented completion speech only. No speech or replay."
                    : UnconfirmedQuietHoursSave;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            { throw new InvalidOperationException(UnconfirmedQuietHoursSave, exception); }
            finally { CancelQuietHoursSuggestion(); }
        });
    }

    private const string UnconfirmedQuietHoursSave = "Quiet-hours save is unconfirmed. Inspect Settings and audit receipts; "
        + "a write may have persisted. No automatic retry or rollback.";

    private static void RequireQuietHoursNativeInput(HostActivity? originalInput)
    {
        if (originalInput is not null && (originalInput.Request.Origin != RequestOrigin.LocalUi
            || originalInput.Request.InvocationId is not null || originalInput.Activity!.IsStopped
            || originalInput.Outcome != HostOperationOutcome.Unknown))
        { throw new InvalidOperationException("Quiet-hours review requires original native input, not relabelled model or voice authority."); }
    }

    private void CancelQuietHoursSuggestion()
    {
        quietHoursSuggestionIdentity = null;
        quietHoursSuggestionReview?.Dispose();
        quietHoursSuggestionReview = null;
    }
}
