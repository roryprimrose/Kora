using Avalonia.Controls;
using Avalonia.Interactivity;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    internal Task AppearanceOperation { get; private set; } = Task.CompletedTask;

    private void OnAppearanceSuggestion(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button button) { return; }
        if (button is { Name: "ReviewAppearanceSuggestion", DataContext: ConversationMessage message }
            && model.CanReviewAppearanceSuggestion)
        {
            args.Handled = true;
            AppearanceOperation = model.ReviewAppearanceSuggestionAsync(message);
        }
        else if (string.Equals(button.Name, "ConfirmAppearanceSuggestion", StringComparison.Ordinal) && model.CanConfirmAppearanceSuggestion)
        {
            args.Handled = true;
            AppearanceOperation = model.ConfirmAppearanceSuggestionAsync();
        }
        else if (string.Equals(button.Name, "CancelAppearanceSuggestion", StringComparison.Ordinal))
        {
            args.Handled = true;
            model.CancelAppearanceSuggestion();
        }
    }
}
