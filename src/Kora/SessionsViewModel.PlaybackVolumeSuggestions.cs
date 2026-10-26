using Kora.Application.Configuration;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private PlaybackVolumeSuggestionReview? playbackVolumeReview;
    public bool CanReviewPlaybackVolumeSuggestion => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld
        && reviewPlaybackVolumeSuggestion is not null && confirmPlaybackVolumeSuggestion is not null;
    public bool CanConfirmPlaybackVolumeSuggestion => CanReviewPlaybackVolumeSuggestion && playbackVolumeReview?.Available == true;
    public string PlaybackVolumeSuggestionDescription => playbackVolumeReview?.Description ?? string.Empty;

    public Task ReviewPlaybackVolumeSuggestionAsync(ConversationMessage message) =>
        ReviewRegisteredSettingSuggestionAsync(message, SettingReviewFamily.Volume);

    public Task ConfirmPlaybackVolumeSuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = playbackVolumeReview ?? throw new InvalidOperationException("Review an exact volume suggestion first.");
        playbackVolumeReview = null;
        try
        {
            var saved = await (confirmPlaybackVolumeSuggestion
                ?? throw new InvalidOperationException("Volume confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = saved ? "Kora-only playback volume saved with committed preference and audit readback. No speech replayed."
                : "Volume save is unconfirmed. Inspect Settings and audit receipts; "
                    + "a write may have persisted. No automatic retry or rollback.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException("Volume save is unconfirmed. Inspect Settings and audit receipts; "
                + "a write may have persisted. No automatic retry or rollback.", exception);
        }
    });

    public void CancelPlaybackVolumeSuggestion(bool reportCancellation = true)
    {
        playbackVolumeReview?.Dispose();
        playbackVolumeReview = null;
        if (reportCancellation)
        {
            conversationStatus = "Volume review cancelled. No new volume write was requested.";
            OnPropertyChanged(nameof(ConversationStatus));
        }
        OnPropertyChanged(nameof(PlaybackVolumeSuggestionDescription));
        OnPropertyChanged(nameof(CanConfirmPlaybackVolumeSuggestion));
        NotifyRegisteredSettings();
    }
}
