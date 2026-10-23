using Avalonia.Controls;
using Avalonia.Interactivity;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    internal Task SpeechSummaryOperation { get; private set; } = Task.CompletedTask;

    private void OnSpeechSummarySuggestion(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button button) { return; }
        if (button is { Name: "ReviewSpeechSummarySuggestion", DataContext: ConversationMessage message }
            && model.CanReviewSpeechSummarySuggestion)
        {
            args.Handled = true;
            SpeechSummaryOperation = model.ReviewSpeechSummarySuggestionAsync(message);
        }
        else if (string.Equals(button.Name, "ConfirmSpeechSummarySuggestion", StringComparison.Ordinal) && model.CanConfirmSpeechSummarySuggestion)
        {
            args.Handled = true;
            SpeechSummaryOperation = model.ConfirmSpeechSummarySuggestionAsync();
        }
        else if (string.Equals(button.Name, "CancelSpeechSummarySuggestion", StringComparison.Ordinal))
        {
            args.Handled = true;
            model.CancelSpeechSummarySuggestion();
        }
    }
}
