using Kora.Application.Configuration;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private InstalledSpeechVoiceSuggestionReview? installedVoiceReview;
    private Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<InstalledSpeechVoiceSuggestionReview>>? reviewInstalledVoiceSuggestion;
    private Func<InstalledSpeechVoiceSuggestionReview, CancellationToken, Task<SpeechApplyResult>>? confirmInstalledVoiceSuggestion;

    internal void BindInstalledSpeechVoiceSuggestions(
        Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<InstalledSpeechVoiceSuggestionReview>> review,
        Func<InstalledSpeechVoiceSuggestionReview, CancellationToken, Task<SpeechApplyResult>> confirm)
    {
        if (closed || conversationBusy) { throw new InvalidOperationException("Cannot rebind installed voice review while closing or an operation is pending."); }
        CancelInstalledSpeechVoiceSuggestion();
        reviewInstalledVoiceSuggestion = review;
        confirmInstalledVoiceSuggestion = confirm;
        NotifyRegisteredSettings();
    }

    public bool CanReviewInstalledSpeechVoiceSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewInstalledVoiceSuggestion is not null && confirmInstalledVoiceSuggestion is not null;
    public bool CanConfirmInstalledSpeechVoiceSuggestion => CanReviewInstalledSpeechVoiceSuggestion && installedVoiceReview?.Available == true;
    public string InstalledSpeechVoiceSuggestionDescription => installedVoiceReview?.Description ?? string.Empty;

    public Task ConfirmInstalledSpeechVoiceSuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = installedVoiceReview ?? throw new InvalidOperationException("Review an exact installed voice suggestion first.");
        installedVoiceReview = null;
        try
        {
            var result = await (confirmInstalledVoiceSuggestion
                ?? throw new InvalidOperationException("Installed voice confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = result.Succeeded
                ? "Installed provider and voice saved with exact persisted/current readback. NEXT separately requested eligible ordinary speech only; no answer replayed."
                : UnconfirmedInstalledVoiceSave + " " + result.Error;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { throw new InvalidOperationException(UnconfirmedInstalledVoiceSave, exception); }
    });

    private const string UnconfirmedInstalledVoiceSave = "Installed voice save is unconfirmed. Inspect Settings and audit receipts; "
        + "bytes may have persisted. No automatic retry or rollback.";

    private void CancelInstalledSpeechVoiceSuggestion()
    {
        registeredSettingReviewIdentity = null;
        installedVoiceReview?.Dispose();
        installedVoiceReview = null;
    }
}
