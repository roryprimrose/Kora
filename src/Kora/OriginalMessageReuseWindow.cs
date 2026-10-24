using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Controls;

namespace Kora;

internal sealed class OriginalMessageReuseWindow : Window
{
    private readonly OriginalMessageReuseReview review;
    private readonly Window sourceOwner;
    private readonly NamedTextBlock attribution;
    private readonly SnapshotComparisonFindPane original;
    private readonly SnapshotComparisonFindPane previous;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool opening;
    private bool accepted;
    private bool erased;

    internal OriginalMessageReuseWindow(Window sourceOwner, OriginalMessageReuseReview review)
    {
        this.sourceOwner = sourceOwner;
        this.review = review;
        Title = "Review original USER text reuse — not task replay";
        Width = 1000; Height = 760; MinWidth = 680; MinHeight = 500;
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
            Close(true);
        };
        cancel.Click += (_, _) => Close(false);
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
            if (args.Key == Key.Escape) { args.Handled = true; Close(false); }
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            timer.Tick -= Tick;
            review.Retired -= Retired;
            sourceOwner.Closed -= Retired;
            Erase();
            Content = null;
            if (!accepted) { review.Dispose(); }
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
        Task<bool> decision;
        try { decision = ShowDialog<bool>(owner); }
        catch
        {
            Erase();
            review.Dispose();
            sourceOwner.Closed -= Retired;
            review.Retired -= Retired;
            timer.Tick -= Tick;
            Content = null;
            throw;
        }
        finally { opening = false; }
        if (!Current()) { Retire(); }
        else { timer.Start(); }
        return decision;
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
        review.Dispose();
        // Reentrant privacy/owner callbacks erase synchronously, but cannot dispose native Show initialization.
        if (!opening) { Close(false); }
    }
}
