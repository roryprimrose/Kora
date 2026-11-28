using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Hosting.SessionRouting;

namespace Kora;

internal sealed class SessionRoutingReadbackView : UserControl, IDisposable
{
    private readonly NativeViewLifetime presentation;
    private readonly Func<bool> admitted;
    private bool closed;
    internal Window Owner { get; }
    internal event EventHandler? Closed;
    internal bool IsCurrent => !closed && admitted()
        && (Owner is not ResponseWindow || presentation.IsPresented);

    internal SessionRoutingReadbackView(Window owner, SessionRoutingReview review, Func<bool> admitted)
    {
        Owner = owner;
        this.admitted = admitted;
        presentation = new(this, owner, admitted);
        presentation.PresentationChanged += (_, _) =>
        { if (Owner is ResponseWindow && !presentation.IsPresented) { Withdraw(); } };
        owner.Closed += OwnerClosed;
        var root = new StackPanel { Margin = new(16), Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = "Approve all - sending to " + review.Preselection!.Destination.Name + " after readback. Say cancel to stop.",
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBox
        {
            Name = "RoutingReadbackBody", Text = review.Body, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 260
        });
        var cancel = new Button { Name = "RoutingReadbackCancel", Content = "Cancel without submitting" };
        cancel.Click += (_, _) => Withdraw();
        root.Children.Add(cancel);
        Content = new ScrollViewer { Content = root, HorizontalAlignment = HorizontalAlignment.Stretch };
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Withdraw(); } };
    }

    internal void Show()
    {
        if (Owner is ResponseWindow primary)
        {
            primary.ShowSessions(expanded: true, activate: false, preservePresentation: true);
            primary.ShowNativeReview(this, "Approve all request readback", this, admitted,
                shown => { if (!shown) { Withdraw(); } }, activate: false);
        }
        else { Owner.Content = this; Owner.ShowActivated = false; Owner.Show(); }
        presentation.Refresh();
    }

    private void OwnerClosed(object? sender, EventArgs args) => Withdraw();

    internal void Withdraw()
    {
        if (closed) { return; }
        closed = true;
        presentation.Dispose();
        Owner.Closed -= OwnerClosed;
        if (Owner is ResponseWindow primary) { primary.RemoveNativeReview(this); }
        else { Owner.Close(); }
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Withdraw();
}
