using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    internal Task RegisteredSettingOperation { get; private set; } = Task.CompletedTask;

    private void OnRegisteredSettingSuggestion(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button button) { return; }
        if (button is { Name: "ReviewRegisteredSettingSuggestion", DataContext: ConversationMessage message }
            && model.CanReviewRegisteredSettingSuggestion)
        {
            args.Handled = true;
            RegisteredSettingOperation = model.ReviewRegisteredSettingSuggestionAsync(message);
        }
        else if (string.Equals(button.Name, "ConfirmRegisteredSettingSuggestion", StringComparison.Ordinal) && model.CanConfirmRegisteredSettingSuggestion)
        {
            args.Handled = true;
            RegisteredSettingOperation = model.ConfirmRegisteredSettingSuggestionAsync();
        }
        else if (string.Equals(button.Name, "CancelRegisteredSettingSuggestion", StringComparison.Ordinal))
        {
            args.Handled = true;
            model.CancelRegisteredSettingSuggestion();
        }
    }
}
