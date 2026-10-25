using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    internal Task PlaybackVolumeOperation { get; private set; } = Task.CompletedTask;

    private void OnPlaybackVolumeSuggestion(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button button) { return; }
        if (button is { Name: "ReviewPlaybackVolumeSuggestion", DataContext: ConversationMessage message }
            && model.CanReviewPlaybackVolumeSuggestion)
        {
            args.Handled = true;
            PlaybackVolumeOperation = model.ReviewPlaybackVolumeSuggestionAsync(message);
        }
        else if (string.Equals(button.Name, "ConfirmPlaybackVolumeSuggestion", StringComparison.Ordinal) && model.CanConfirmPlaybackVolumeSuggestion)
        {
            args.Handled = true;
            PlaybackVolumeOperation = model.ConfirmPlaybackVolumeSuggestionAsync();
        }
        else if (string.Equals(button.Name, "CancelPlaybackVolumeSuggestion", StringComparison.Ordinal))
        {
            args.Handled = true;
            model.CancelPlaybackVolumeSuggestion();
        }
    }
}
