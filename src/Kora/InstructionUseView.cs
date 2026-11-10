using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Kora;

internal sealed class InstructionUseView : UserControl, IDisposable
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime lifetime;
    private bool disposed;
    internal event EventHandler? Closed;
    internal event EventHandler? PresentationLost;
    internal Func<bool> CapturePresentation() => lifetime.Capture();
    internal TextBox Review { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, MinHeight = 280 };
    internal TextBox Result { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, MinHeight = 140 };
    internal Button Confirm { get; } = new() { Content = "Submit this one reviewed local request" };
    internal Button Cancel { get; } = new() { Content = "Cancel / stop and join owned work" };
    internal TextBlock Status { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    internal InstructionUseView(Window owner, Action<string> run, Func<bool> admitted)
    {
        Owner = owner;
        lifetime = new(this, owner, () => !disposed && admitted());
        lifetime.PresentationChanged += (_, _) =>
        {
            if (!lifetime.IsPresented)
            {
                Clear();
                PresentationLost?.Invoke(this, EventArgs.Empty);
            }
        };
        owner.Closed += OwnerClosed;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Review the complete pinned source, original request, target and original channel below. "
                + "Nothing is submitted until you separately confirm. Cancel stops and joins owned work. "
                + "Hiding a confirmed request clears this presentation without stopping its admitted task.",
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

    internal void Close() => Dispose();
    private void OwnerClosed(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        Clear();
        Owner.Closed -= OwnerClosed;
        lifetime.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
        if (Owner is ResponseWindow primary) { primary.RemoveNativeReview(this); }
        else if (Owner is InstructionUseWindow) { Owner.Close(); }
    }
}
