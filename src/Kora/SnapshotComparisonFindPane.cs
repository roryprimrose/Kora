using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed class SnapshotComparisonFindPane : DockPanel, IDisposable
{
    private readonly NamedTextBlock heading;
    private readonly TextBox source;
    private readonly TextBox query;
    private readonly NamedTextBlock outcome = new() { TextWrapping = TextWrapping.Wrap };
    private readonly NamedTextBlock span = new() { Focusable = true, IsVisible = false, FontFamily = new("Consolas") };
    private readonly Button first = new() { Content = "Find first" };
    private readonly Button next = new() { Content = "Find next", IsEnabled = false };
    private readonly Button previous = new() { Content = "Find previous", IsEnabled = false };
    private readonly Button clear = new() { Content = "Clear Find" };
    private Func<bool>? current;
    private Action? retire;
    private string? text;
    private SnapshotLiteralSpan? selected;
    private long generation;
    private bool disposed;

    internal SnapshotComparisonFindPane(string label, string? text, Func<bool> current, Action retire)
    {
        this.text = text;
        this.current = current;
        this.retire = retire;
        Margin = new(4);
        var chrome = new StackPanel { Spacing = 6 };
        heading = new NamedTextBlock
        {
            Text = label + (text is null ? " (missing/new baseline; no searchable source)" : string.Empty),
            TextWrapping = TextWrapping.Wrap,
        };
        chrome.Children.Add(heading);
        chrome.Children.Add(new NamedTextBlock
        {
            Text = "Case-sensitive ordinal literal Find; exact spaces, one nonoverlapping match, no wrap or count. "
                + "Within this pane: Ctrl+F focuses query; Enter finds first; F3 next; Shift+F3 previous. No clipboard transfer.",
            TextWrapping = TextWrapping.Wrap,
        });
        query = new TextBox
        {
            PlaceholderText = $"Literal Find: maximum {LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 characters / "
                + $"{LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes",
            AcceptsReturn = false, ContextMenu = null, IsUndoEnabled = false, IsEnabled = text is not null,
        };
        AutomationProperties.SetName(query, label + " case-sensitive ordinal literal Find query; one line; "
            + $"{LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 characters / {LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes maximum");
        AutomationProperties.SetName(outcome, label + " literal Find outcome and exact UTF-16 location");
        AutomationProperties.SetName(span, label + " exact inert literal match");
        source = new TextBox
        {
            Text = text ?? string.Empty, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new("Consolas"), ContextMenu = null, IsUndoEnabled = false,
        };
        AutomationProperties.SetName(source, label + (text is null ? " absent; no source" : " complete inert read-only historical source"));
        first.IsEnabled = text is not null;
        clear.IsEnabled = text is not null;
        AutomationProperties.SetName(first, label + " find first literal; no wrap");
        AutomationProperties.SetName(next, label + " find next literal; no wrap");
        AutomationProperties.SetName(previous, label + " find previous literal; no wrap");
        AutomationProperties.SetName(clear, label + " clear private Find query and match");
        first.Click += OnFirst;
        next.Click += OnNext;
        previous.Click += OnPrevious;
        clear.Click += OnClear;
        query.PropertyChanged += OnQueryChanged;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { first, next, previous, clear })
        {
            button.Margin = new(0, 0, 6, 0);
            actions.Children.Add(button);
        }
        chrome.Children.Add(query);
        chrome.Children.Add(actions);
        chrome.Children.Add(outcome);
        chrome.Children.Add(new ScrollViewer { Content = span, MaxHeight = 100 });
        var viewport = new ScrollViewer { Content = chrome, MaxHeight = 200 };
        SetDock(viewport, Dock.Top);
        Children.Add(viewport);
        Children.Add(source);
        AddHandler(KeyDownEvent, BlockTransferKeys, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, BlockContext, RoutingStrategies.Tunnel);
        AddHandler(TextBox.CopyingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AddHandler(TextBox.CuttingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AddHandler(TextBox.PastingFromClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
    }

    private bool EnsureCurrent()
    {
        if (disposed) { return false; }
        if (current?.Invoke() == true && !disposed) { return true; }
        var close = retire;
        Dispose();
        close?.Invoke();
        return false;
    }

    private void OnQueryChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty && EnsureCurrent()) { ClearMatch(); }
    }

    private void OnFirst(object? sender, RoutedEventArgs args) => Find(SnapshotFindDirection.First);
    private void OnNext(object? sender, RoutedEventArgs args) => Find(SnapshotFindDirection.Next);
    private void OnPrevious(object? sender, RoutedEventArgs args) => Find(SnapshotFindDirection.Previous);
    private void OnClear(object? sender, RoutedEventArgs args)
    {
        if (!EnsureCurrent()) { return; }
        ClearMatch();
        query.Text = string.Empty;
    }

    private void Find(SnapshotFindDirection direction)
    {
        if (!EnsureCurrent() || text is not { } snapshot) { return; }
        var literal = query.Text ?? string.Empty;
        var cursor = selected;
        ClearMatch();
        var operation = generation;
        bool IsCurrent() => EnsureCurrent() && operation == generation;
        if (!SnapshotLiteralFind.IsValidQuery(literal))
        {
            outcome.Text = "Invalid literal Find query: enter nonempty, bounded, well-formed one-line text without unsafe controls. Nothing was truncated.";
            return;
        }
        if (direction != SnapshotFindDirection.First && cursor is null)
        {
            outcome.Text = "Find first after editing or clearing the query; no current match to navigate.";
            return;
        }
        var match = SnapshotLiteralFind.Find(snapshot, literal, direction, cursor);
        if (!IsCurrent()) { return; }
        selected = match ?? (direction == SnapshotFindDirection.First ? null : cursor);
        next.IsEnabled = previous.IsEnabled = selected is not null;
        outcome.Text = match is null
            ? direction == SnapshotFindDirection.First ? "No literal match in this exact pane snapshot."
                : direction == SnapshotFindDirection.Next ? "End of forward direction; no wrap. Earlier matches may remain."
                    : "End of backward direction; no wrap. Later matches may remain."
            : $"One exact literal match: UTF-16 [{match.Start}, {match.Start + match.Length}); end exclusive.";
        if (!IsCurrent() || match is null) { return; }
        span.Text = snapshot.Substring(match.Start, match.Length);
        if (!IsCurrent()) { return; }
        span.IsVisible = true;
        UpdateLayout();
        if (!IsCurrent()) { return; }
        span.BringIntoView();
        if (!IsCurrent()) { return; }
        var focused = span.Focus();
        if (!IsCurrent()) { return; }
        if (!focused)
        { outcome.Text += " Exact match focus unavailable; use Tab to reach the match."; }
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        var control = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != KeyModifiers.None;
        var shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!((control && args.Key == Key.F) || args.Key == Key.F3
            || (args.Key == Key.Enter && ReferenceEquals(args.Source, query)))) { return; }
        args.Handled = true;
        if (!EnsureCurrent()) { return; }
        if (control && args.Key == Key.F) { query.Focus(); return; }
        Find(args.Key == Key.F3 ? shift ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next : SnapshotFindDirection.First);
    }

    private static void BlockContext(object? sender, ContextRequestedEventArgs args) => args.Handled = true;
    private static void BlockTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
    private static void BlockTransferKeys(object? sender, KeyEventArgs args)
    {
        var control = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != KeyModifiers.None;
        var shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((control && args.Key is Key.C or Key.X or Key.V or Key.Insert)
            || (shift && args.Key is Key.Insert or Key.Delete) || args.Key == Key.Apps || (shift && args.Key == Key.F10))
        { args.Handled = true; }
    }

    private void ClearMatch()
    {
        generation++;
        selected = null;
        span.Text = null;
        span.IsVisible = false;
        outcome.Text = null;
        next.IsEnabled = previous.IsEnabled = false;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        current = null;
        retire = null;
        text = null;
        first.Click -= OnFirst;
        next.Click -= OnNext;
        previous.Click -= OnPrevious;
        clear.Click -= OnClear;
        query.PropertyChanged -= OnQueryChanged;
        ClearMatch();
        query.Text = string.Empty;
        source.Text = string.Empty;
        heading.Text = string.Empty;
        AutomationProperties.SetName(source, "Retired comparison source");
        AutomationProperties.SetName(query, "Retired comparison query");
        AutomationProperties.SetName(outcome, "Retired comparison Find outcome");
        AutomationProperties.SetName(span, "Retired comparison match");
        foreach (var button in new[] { first, next, previous, clear })
        { AutomationProperties.SetName(button, "Retired comparison action"); }
        first.IsEnabled = clear.IsEnabled = query.IsEnabled = false;
        RemoveHandler(KeyDownEvent, OnKeyDown);
        // Clipboard/context blockers stay on a retained retired control; they hold no private source.
    }
}
