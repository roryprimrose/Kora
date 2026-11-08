using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private OrdinaryTurnDeadlineSuggestionReview? ordinaryDeadlineSuggestionReview;
    private Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<OrdinaryTurnDeadlineSuggestionReview>>? reviewOrdinaryDeadlineSuggestion;
    private Func<OrdinaryTurnDeadlineSuggestionReview, CancellationToken, Task<bool>>? confirmOrdinaryDeadlineSuggestion;
    internal Func<Func<bool>>? CaptureOrdinaryDeadlineNativeLifetime { get; set; }

    internal void BindOrdinaryTurnDeadlineSuggestions(
        Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<OrdinaryTurnDeadlineSuggestionReview>> review,
        Func<OrdinaryTurnDeadlineSuggestionReview, CancellationToken, Task<bool>> confirm)
    {
        if (closed || conversationBusy) { throw new InvalidOperationException("Cannot rebind deadline review during an operation or closure."); }
        CancelOrdinaryDeadlineSuggestion();
        reviewOrdinaryDeadlineSuggestion = review;
        confirmOrdinaryDeadlineSuggestion = confirm;
        NotifyRegisteredSettings();
    }

    private bool CanReviewOrdinaryDeadlineSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewOrdinaryDeadlineSuggestion is not null && confirmOrdinaryDeadlineSuggestion is not null;
    private bool CanConfirmOrdinaryDeadlineSuggestion => CanReviewOrdinaryDeadlineSuggestion && ordinaryDeadlineSuggestionReview?.Available == true;
    private string OrdinaryDeadlineSuggestionDescription => ordinaryDeadlineSuggestionReview?.Description ?? string.Empty;

    private Task ConfirmOrdinaryDeadlineSuggestionAsync()
    {
        var originalInput = HostActivity.Current;
        return RunConversationAsync(async (_, _) =>
        {
            RequireOrdinaryDeadlineNativeInput(originalInput);
            using var review = ordinaryDeadlineSuggestionReview ?? throw new InvalidOperationException("Review the exact deadline answer first.");
            ordinaryDeadlineSuggestionReview = null;
            try
            {
                var saved = await (confirmOrdinaryDeadlineSuggestion
                    ?? throw new InvalidOperationException("Deadline confirmation is unavailable."))(review, lifetime.Token);
                conversationStatus = saved
                    ? "Future ordinary task deadline saved with committed preference and audit readback. Running turns and earlier context/provider/tool ceilings are unchanged."
                    : UnconfirmedOrdinaryDeadlineSave;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            { throw new InvalidOperationException(UnconfirmedOrdinaryDeadlineSave, exception); }
            finally { CancelOrdinaryDeadlineSuggestion(); }
        });
    }

    private const string UnconfirmedOrdinaryDeadlineSave = "Future task deadline save is unconfirmed. Inspect Settings and audit receipts; "
        + "a write may have persisted. No automatic retry or rollback.";

    private static void RequireOrdinaryDeadlineNativeInput(HostActivity? originalInput)
    {
        if (originalInput is not null && (originalInput.Request.Origin != RequestOrigin.LocalUi
            || originalInput.Request.InvocationId is not null || originalInput.Activity!.IsStopped
            || originalInput.Outcome != HostOperationOutcome.Unknown))
        { throw new InvalidOperationException("Deadline review requires original native input, not relabelled model or voice authority."); }
    }

    private void CancelOrdinaryDeadlineSuggestion()
    {
        registeredSettingReviewIdentity = null;
        ordinaryDeadlineSuggestionReview?.Dispose();
        ordinaryDeadlineSuggestionReview = null;
    }
}
