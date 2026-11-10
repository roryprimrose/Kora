using Avalonia.Controls;
using Avalonia.Threading;

namespace Kora;

internal sealed class PrivateReviewView : UserControl, IDisposable
{
    private readonly NativeViewLifetime presentation;
    private readonly Func<bool> admitted;
    private bool closed;
    internal Window Owner { get; }
    internal bool IsPresented => presentation.IsPresented;
    internal event EventHandler? Closed;

    internal PrivateReviewView(Window owner, Control content, Func<bool> admitted)
    {
        Owner = owner;
        Content = content;
        this.admitted = admitted;
        presentation = new(this, owner, () => !closed && admitted());
        presentation.PresentationChanged += (_, _) => { if (!presentation.IsPresented) { Dispose(); } };
        owner.Closed += OnOwnerClosed;
    }

    internal void Show(string title)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (!admitted()) { throw new InvalidOperationException("The original private review admission expired."); }
        if (Owner is ResponseWindow primary)
        {
            primary.ShowSessions(expanded: true, preservePresentation: true);
            primary.ShowNativeReview(this, title, this, admitted, shown => { if (!shown) { Dispose(); } });
        }
        else
        {
            Owner.Title = title;
            Owner.Content = this;
            Owner.Show();
        }
        presentation.Refresh();
        Owner.Activate();
    }

    internal void Activate() => Owner.Activate();
    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (closed) { return; }
        closed = true;
        IsEnabled = false;
        Content = null;
        Owner.Closed -= OnOwnerClosed;
        presentation.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
        if (Owner is ResponseWindow primary)
        { Dispatcher.UIThread.Post(() => primary.RemoveNativeReview(this)); }
        else
        {
            Owner.Content = null;
            Owner.Close();
        }
    }
}
