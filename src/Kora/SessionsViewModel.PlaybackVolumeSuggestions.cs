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

    public Task ReviewPlaybackVolumeSuggestionAsync(ConversationMessage message) => RunConversationAsync(async (target, live) =>
    {
        CancelPlaybackVolumeSuggestion(reportCancellation: false);
        if (reviewPlaybackVolumeSuggestion is null || !access.CanControl || !target.Authority.IsActive
            || message.Generation != target.Authority.Generation || !ConversationMessages.Any(row => ReferenceEquals(row, message)))
        { throw new InvalidOperationException("Select an exact retained answer from this Active conversation for volume review."); }
        var page = conversationPage;
        var completed = completedConversationMessage;
        bool Current() => live() && access.CanControl && !conversationSourceHeld
            && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
        async Task Validate(CancellationToken token)
        {
            _ = await conversation!.ReadSettingSuggestionAsync(target, message, Current, token).ConfigureAwait(false);
        }
        var review = await reviewPlaybackVolumeSuggestion(message, Current, Validate, lifetime.Token);
        if (!Current()) { review.Dispose(); throw new OperationCanceledException("Volume suggestion source revoked."); }
        playbackVolumeReview = review;
        conversationStatus = "Review current/proposed shared Kora-only volume below. Nothing changed; confirmation is separate.";
    });

    public Task ConfirmPlaybackVolumeSuggestionAsync() => RunConversationAsync(async (_, _) =>
    {
        using var review = playbackVolumeReview ?? throw new InvalidOperationException("Review an exact volume suggestion first.");
        playbackVolumeReview = null;
        try
        {
            var saved = await (confirmPlaybackVolumeSuggestion
                ?? throw new InvalidOperationException("Volume confirmation is unavailable."))(review, lifetime.Token);
            conversationStatus = saved ? "Kora-only playback volume saved with committed preference and audit readback. No speech replayed."
                : "Volume not confirmed. Inspect Settings and audit receipts before a fresh review.";
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
    }
}
