using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private ClipboardSnapshot? findSource;
    private Func<bool>? findCurrent;
    private TextBox? findQuery;
    private NamedTextBlock? findStatus;
    private NamedTextBlock? findSpan;
    private Button? findNext;
    private Button? findPrevious;
    private ClipboardLiteralMatch? findMatch;
    private long findGeneration;
    private long matchGeneration;

    private void AddFind(StackPanel chrome, ClipboardSnapshot snapshot, Func<bool> isCurrent)
    {
        findSource = snapshot;
        findCurrent = isCurrent;
        var generation = findGeneration;
        var query = new TextBox
        {
            AcceptsReturn = false,
            PlaceholderText = $"Literal Find: {LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 characters / "
                + $"{LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes maximum",
            ContextMenu = null,
        };
        findQuery = query;
        query.AddHandler(TextBox.CopyingToClipboardEvent, BlockClipboardTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        query.AddHandler(TextBox.CuttingToClipboardEvent, BlockClipboardTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        query.AddHandler(TextBox.PastingFromClipboardEvent, BlockClipboardTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AutomationProperties.SetName(query, "Case-sensitive literal Find in this exact clipboard snapshot only");
        query.PropertyChanged += (_, args) =>
        {
            if (args.Property != TextBox.TextProperty || generation != findGeneration) { return; }
            if (!EnsureFindCurrent(generation)) { return; }
            ClearFindMatch();
        };
        findStatus = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(findStatus, "Clipboard literal Find outcome and exact UTF-16 location");
        findSpan = new NamedTextBlock { Focusable = true, FontFamily = source.FontFamily, IsVisible = false };
        if (typographyHost is not null) { NativeReaderTypography.Bind(findSpan, typographyHost); }
        AutomationProperties.SetName(findSpan, "Exact inert clipboard literal match");
        Button Action(string caption, SnapshotFindDirection direction)
        {
            var button = new Button { Content = caption };
            AutomationProperties.SetName(button, caption + " literal in this clipboard snapshot; no wrap");
            button.Command = new AsyncCommand(() =>
            {
                FindLiteral(direction, generation);
                return Task.CompletedTask;
            }, exception =>
            {
                if (generation != findGeneration) { return; }
                ClearFindMatch();
                findStatus!.Text = "Literal Find rejected or unavailable. Enter bounded, well-formed one-line text; nothing was truncated.";
                reportFailure(exception);
            });
            return button;
        }
        var first = Action("Find first", SnapshotFindDirection.First);
        findNext = Action("Find next", SnapshotFindDirection.Next);
        findPrevious = Action("Find previous", SnapshotFindDirection.Previous);
        findNext.IsEnabled = false;
        findPrevious.IsEnabled = false;
        var clear = new Button { Content = "Clear literal Find" };
        AutomationProperties.SetName(clear, "Clear clipboard literal Find query and match");
        clear.Click += (_, _) =>
        {
            if (!EnsureFindCurrent(generation)) { return; }
            ClearFindMatch();
            query.Text = null;
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(first);
        actions.Children.Add(findNext);
        actions.Children.Add(findPrevious);
        actions.Children.Add(clear);
        chrome.Children.Add(new NamedTextBlock
        {
            Text = "Native literal Find only: exact case and spaces, one nonoverlapping match, no wrap or occurrence count. "
                + "Enter finds first; F3 finds next; Shift+F3 finds previous; Ctrl+F focuses the query. No clipboard copy/paste.",
            TextWrapping = TextWrapping.Wrap,
        });
        chrome.Children.Add(query);
        chrome.Children.Add(actions);
        chrome.Children.Add(findStatus);
        chrome.Children.Add(new ScrollViewer { Content = findSpan, MaxHeight = 100 });
        AddHandler(KeyDownEvent, OnFindKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnFindContextRequested, RoutingStrategies.Tunnel);
    }

    private bool EnsureFindCurrent(long generation)
    {
        if (generation != findGeneration) { return false; }
        if (findSource is not null && findCurrent?.Invoke() == true) { return true; }
        ClearAndClose();
        return false;
    }

    private void FindLiteral(SnapshotFindDirection direction, long generation)
    {
        if (!EnsureFindCurrent(generation)) { return; }
        var snapshot = findSource!;
        var query = findQuery!.Text ?? string.Empty;
        var selected = findMatch;
        ClearFindMatch();
        var operation = matchGeneration;
        bool Current() => EnsureFindCurrent(generation) && operation == matchGeneration
            && ReferenceEquals(findSource, snapshot) && string.Equals(findQuery!.Text ?? string.Empty, query, StringComparison.Ordinal);
        var match = ClipboardLiteralFind.Find(snapshot, query, direction, selected);
        if (!Current()) { return; }
        findMatch = match ?? (direction == SnapshotFindDirection.First ? null : selected);
        findNext!.IsEnabled = findMatch is not null;
        findPrevious!.IsEnabled = findMatch is not null;
        findStatus!.Text = match is null
            ? direction == SnapshotFindDirection.First ? "No literal match in this exact clipboard snapshot."
                : direction == SnapshotFindDirection.Next ? "End of forward direction; no wrap. Earlier matches may remain."
                    : "End of backward direction; no wrap. Later matches may remain."
            : $"One exact literal match: UTF-16 [{match.Span.Start}, {match.Span.Start + match.Span.Length}); end exclusive.";
        if (!Current() || match is null) { return; }
        findSpan!.Text = snapshot.Text.Substring(match.Span.Start, match.Span.Length);
        findSpan.IsVisible = true;
        UpdateLayout();
        if (!Current()) { return; }
        findSpan.BringIntoView();
        if (!Current()) { return; }
        if (!findSpan.Focus()) { throw new InvalidOperationException("Exact clipboard literal match focus is unavailable."); }
        if (!Current()) { return; }
    }

    private void OnFindKeyDown(object? sender, KeyEventArgs args)
    {
        var control = args.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((control && args.Key is Key.C or Key.X or Key.V or Key.Insert)
            || (shift && args.Key is Key.Insert or Key.Delete) || args.Key == Key.Apps
            || (shift && args.Key == Key.F10))
        {
            args.Handled = true;
            return;
        }
        if (!((control && args.Key == Key.F) || args.Key == Key.F3
            || (args.Key == Key.Enter && ReferenceEquals(args.Source, findQuery)))) { return; }
        args.Handled = true;
        var generation = findGeneration;
        if (!EnsureFindCurrent(generation)) { return; }
        if (control && args.Key == Key.F) { findQuery!.Focus(); return; }
        try
        {
            FindLiteral(args.Key == Key.F3 ? shift ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next
                : SnapshotFindDirection.First, generation);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            if (generation != findGeneration) { return; }
            ClearFindMatch();
            findStatus!.Text = "Literal Find rejected or unavailable. Enter bounded, well-formed one-line text; nothing was truncated.";
            reportFailure(exception);
        }
    }

    private static void OnFindContextRequested(object? sender, ContextRequestedEventArgs args) => args.Handled = true;
    private static void BlockClipboardTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;

    private void ClearFindMatch()
    {
        matchGeneration++;
        findMatch = null;
        if (findSpan is not null) { findSpan.Text = null; findSpan.IsVisible = false; }
        if (findStatus is not null) { findStatus.Text = null; }
        if (findNext is not null) { findNext.IsEnabled = false; }
        if (findPrevious is not null) { findPrevious.IsEnabled = false; }
    }

    private void ClearFindState()
    {
        findGeneration++;
        ClearFindMatch();
        findSource = null;
        findCurrent = null;
        if (findQuery is not null)
        {
            findQuery.Text = null;
            findQuery.RemoveHandler(TextBox.CopyingToClipboardEvent, BlockClipboardTransfer);
            findQuery.RemoveHandler(TextBox.CuttingToClipboardEvent, BlockClipboardTransfer);
            findQuery.RemoveHandler(TextBox.PastingFromClipboardEvent, BlockClipboardTransfer);
        }
        findQuery = null;
        findSpan = null;
        findStatus = null;
        findNext = null;
        findPrevious = null;
        RemoveHandler(KeyDownEvent, OnFindKeyDown);
        RemoveHandler(ContextRequestedEvent, OnFindContextRequested);
    }
}
