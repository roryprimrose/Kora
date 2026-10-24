using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Kora;

internal sealed class InstructionUseWindow : Window
{
    internal TextBox Review { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, MinHeight = 280 };
    internal TextBox Result { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, MinHeight = 140 };
    internal Button Confirm { get; } = new() { Content = "Submit this one reviewed local request" };
    internal Button Cancel { get; } = new() { Content = "Cancel / stop and join owned work" };
    internal TextBlock Status { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    internal InstructionUseWindow(Action<string> run)
    {
        Title = "Review one owned instruction request";
        Width = 850;
        Height = 760;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Review the complete pinned source, original request, target and original channel below. "
                + "Nothing is submitted until you separately confirm. Closing cancels and joins owned work.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(Review);
        panel.Children.Add(Confirm);
        panel.Children.Add(Cancel);
        panel.Children.Add(Status);
        panel.Children.Add(Result);
        Confirm.Click += (_, _) => run("Confirm");
        Cancel.Click += (_, _) => run("Cancel");
        Content = new ScrollViewer { Content = panel };
    }

    internal void Clear()
    {
        Review.Text = Result.Text = Status.Text = string.Empty;
        Confirm.IsEnabled = false;
    }
}
