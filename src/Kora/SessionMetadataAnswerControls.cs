using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kora;

internal sealed class SessionMetadataAnswerControls : StackPanel
{
    internal TextBox Caller { get; } = new() { PlaceholderText = "Exact caller conversation ID" };
    internal TextBox Subject { get; } = new() { PlaceholderText = "Exact inspected session ID" };
    internal TextBox Question { get; } = new() { PlaceholderText = "Original metadata question", AcceptsReturn = true, MinHeight = 70 };
    internal TextBox Preview { get; } = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180 };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal Button Review { get; } = new() { Content = "Review exact metadata locally" };
    internal Button Confirm { get; } = new() { Content = "Confirm this one LOCAL disclosure", IsEnabled = false };
    internal Button Cancel { get; } = new() { Content = "Cancel / close" };

    internal SessionMetadataAnswerControls()
    {
        Spacing = 8;
        Margin = new(16);
        Children.Add(new TextBlock { Text = "Caller conversation (answer retained here)", TextWrapping = TextWrapping.Wrap });
        Children.Add(Caller);
        Children.Add(new TextBlock { Text = "Inspected subject (passive; no navigation or renewal)", TextWrapping = TextWrapping.Wrap });
        Children.Add(Subject);
        Children.Add(Question);
        Children.Add(Review);
        Children.Add(Preview);
        Children.Add(Status);
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Confirm, Cancel } });
    }

    internal void Clear()
    {
        Preview.Text = string.Empty;
        Confirm.IsEnabled = false;
    }
}
