using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private DetailPresentationSuggestionReview? detailPresentationSuggestionReview;
    private Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<DetailPresentationSuggestionReview>>? reviewDetailPresentationSuggestion;
    private Func<DetailPresentationSuggestionReview, CancellationToken, Task<bool>>? confirmDetailPresentationSuggestion;
    internal Func<Func<bool>>? CaptureDetailPresentationNativeLifetime { get; set; }

    internal void BindDetailPresentationSuggestions(
        Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<DetailPresentationSuggestionReview>> review,
        Func<DetailPresentationSuggestionReview, CancellationToken, Task<bool>> confirm)
    {
        if (closed || conversationBusy) { throw new InvalidOperationException("Cannot rebind Details review during an operation or closure."); }
        CancelDetailPresentationSuggestion();
        reviewDetailPresentationSuggestion = review;
        confirmDetailPresentationSuggestion = confirm;
        NotifyRegisteredSettings();
    }

    private bool CanReviewDetailPresentationSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewDetailPresentationSuggestion is not null && confirmDetailPresentationSuggestion is not null;
    private bool CanConfirmDetailPresentationSuggestion => CanReviewDetailPresentationSuggestion && detailPresentationSuggestionReview?.Available == true;
    private string DetailPresentationSuggestionDescription => detailPresentationSuggestionReview?.Description ?? string.Empty;

    private Task ConfirmDetailPresentationSuggestionAsync()
    {
        var originalInput = HostActivity.Current;
        return RunConversationAsync(async (_, _) =>
        {
            RequireDetailPresentationNativeInput(originalInput);
            using var review = detailPresentationSuggestionReview ?? throw new InvalidOperationException("Review the exact Details answer first.");
            detailPresentationSuggestionReview = null;
            try
            {
                var saved = await (confirmDetailPresentationSuggestion
                    ?? throw new InvalidOperationException("Details confirmation is unavailable."))(review, lifetime.Token);
                conversationStatus = saved
                    ? "Detail presentation saved with committed preference and audit readback. Future eligible publications only; no answer opened or replayed."
                    : UnconfirmedDetailPresentationSave;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            { throw new InvalidOperationException(UnconfirmedDetailPresentationSave, exception); }
            finally { CancelDetailPresentationSuggestion(); }
        });
    }

    private const string UnconfirmedDetailPresentationSave = "Detail presentation save is unconfirmed. Inspect Settings and audit receipts; "
        + "a write may have persisted. No automatic retry or rollback.";

    private static void RequireDetailPresentationNativeInput(HostActivity? originalInput)
    {
        if (originalInput is not null && (originalInput.Request.Origin != RequestOrigin.LocalUi
            || originalInput.Request.InvocationId is not null || originalInput.Activity!.IsStopped
            || originalInput.Outcome != HostOperationOutcome.Unknown))
        { throw new InvalidOperationException("Details review requires original native input, not relabelled model or voice authority."); }
    }

    private void CancelDetailPresentationSuggestion()
    {
        registeredSettingReviewIdentity = null;
        detailPresentationSuggestionReview?.Dispose();
        detailPresentationSuggestionReview = null;
    }
}
