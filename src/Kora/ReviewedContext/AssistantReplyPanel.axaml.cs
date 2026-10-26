using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Kora;

public sealed partial class AssistantReplyPanel : UserControl
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public AssistantReplyPanel()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(Button.ClickEvent, OnReplyCommand);
    }

    private void OnReplyCommand(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SessionsViewModel model || args.Source is not Button button) { return; }
        if (string.Equals(button.Name, "ReviewAssistantReply", StringComparison.Ordinal))
        { args.Handled = true; Operation = model.ReviewAssistantReplyAsync(); }
        else if (string.Equals(button.Name, "ConfirmAssistantReply", StringComparison.Ordinal))
        { args.Handled = true; Operation = model.ConfirmAssistantReplyAsync(); }
        else if (string.Equals(button.Name, "CancelAssistantReply", StringComparison.Ordinal))
        { args.Handled = true; model.CancelAssistantReply(); }
    }
}
