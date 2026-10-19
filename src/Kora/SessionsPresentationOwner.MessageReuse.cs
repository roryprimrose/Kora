using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    internal Task ReuseOperation { get; private set; } = Task.CompletedTask;

    private void OnOriginalMessageReuse(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button { Name: "ReviewOriginalMessageReuse", DataContext: ConversationMessage message }
            || !model.CanReviewOriginalMessageReuse) { return; }
        args.Handled = true;
        ReuseOperation = model.ReviewOriginalMessageReuseAsync(message);
    }
}
