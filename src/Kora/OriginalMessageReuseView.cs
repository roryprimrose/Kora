using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Controls;

namespace Kora;

internal sealed class OriginalMessageReuseView : UserControl, IDisposable
{
    private readonly Window owner;
    private readonly NativeViewLifetime lifetime;
    private readonly TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal event EventHandler? CloseRequested;
    internal bool Accepted => accepted;
    private bool disposed;
    private readonly OriginalMessageReuseReview review;
    private readonly Window sourceOwner;
    private readonly NamedTextBlock attribution;
    private readonly SnapshotComparisonFindPane original;
    private readonly SnapshotComparisonFindPane previous;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool opening;
    private bool accepted;
    private bool erased;

    internal OriginalMessageReuseView(Window sourceOwner, Window owner, OriginalMessageReuseReview review)
    {
        this.sourceOwner = sourceOwner;
        this.review = review;
        this.owner = owner;
        lifetime = new(this, owner, () => !disposed);
        lifetime.PresentationChanged += (_, _) =>
        {
            if (lifetime.IsPresented) { timer.Start(); }
            else if (!opening) { Retire(); }
        };
        var root = new DockPanel { Margin = new(14) };
        attribution = new() { Text = review.Attribution, TextWrapping = TextWrapping.Wrap };
        var disclosure = new ScrollViewer { Content = attribution, MaxHeight = 210 };
        DockPanel.SetDock(disclosure, Dock.Top);
        root.Children.Add(disclosure);
        var actions = new StackPanel { Spacing = 8, Margin = new(0, 10) };
        var confirm = new Button
        {
            Name = "ConfirmOriginalMessageReuse",
            Content = review.RequiresReplacement ? "Replace reviewed existing New request memory with this exact USER text"
                : "Copy this exact USER text to New request memory only",
        };
        var cancel = new Button { Name = "CancelOriginalMessageReuse", Content = "Keep existing memory / cancel reuse" };
        confirm.Click += (_, _) =>
        {
            if (!Current()) { Retire(); return; }
            accepted = true;
            decision.TrySetResult(true);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        cancel.Click += (_, _) => { decision.TrySetResult(false); CloseRequested?.Invoke(this, EventArgs.Empty); };
        actions.Children.Add(confirm);
        actions.Children.Add(cancel);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        var panes = new Grid { ColumnDefinitions = new("*,*") };
        original = new("Complete exact retained original USER body (historical data)", review.Body, Current, Retire);
        previous = new("Complete existing New request memory to keep or explicitly replace", review.PreviousBody, Current, Retire);
        Grid.SetColumn(previous, 1);
        panes.Children.Add(original); panes.Children.Add(previous);
        root.Children.Add(panes);
        Content = root;
        review.Retired += Retired;
        sourceOwner.Closed += Retired;
        timer.Tick += Tick;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; decision.TrySetResult(false); CloseRequested?.Invoke(this, EventArgs.Empty); }
        };
    }

    private bool Current() => !erased && sourceOwner.IsVisible && review.Available;
    private void Tick(object? sender, EventArgs args) { if (!Current()) { Retire(); } }
    private void Retired(object? sender, EventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(Retire); }
        else { Retire(); }
    }

    internal Task<bool> Open(Window owner)
    {
        opening = true;
        try
        {
            if (owner is not ResponseWindow primary) { throw new InvalidOperationException("This original-text review requires its actual owning workbench."); }
            primary.ShowNativeReview(this, "Review original USER text reuse - not task replay", this,
                Current, shown => { if (!shown && !accepted) { Retire(); } });
        }
        catch
        {
            Dispose();
            throw;
        }
        finally { opening = false; }
        if (!Current()) { Retire(); }
        else { timer.Start(); }
        return AwaitDecision();
    }

    private async Task<bool> AwaitDecision()
    {
#pragma warning disable VSTHRD003 // This view owns the native decision completion signalled by its exact confirm/cancel controls.
        try { return await decision.Task; }
#pragma warning restore VSTHRD003
        finally { Dispose(); }
    }

    private void Erase()
    {
        if (erased) { return; }
        erased = true;
        original.Dispose();
        previous.Dispose();
        attribution.Text = string.Empty;
    }

    private void Retire()
    {
        Erase();
        if (!accepted) { review.Dispose(); }
        // Reentrant privacy/owner callbacks erase synchronously, but cannot dispose native Show initialization.
        decision.TrySetResult(false);
        if (!opening) { CloseRequested?.Invoke(this, EventArgs.Empty); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        timer.Stop();
        timer.Tick -= Tick;
        review.Retired -= Retired;
        sourceOwner.Closed -= Retired;
        lifetime.Dispose();
        if (owner is ResponseWindow primary) { primary.RemoveNativeReview(this); }
        Erase();
        Content = null;
        if (!accepted) { review.Dispose(); }
        decision.TrySetResult(false);
    }
}
