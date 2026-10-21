using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class OwnedInstructionEditorWindow
{
    internal readonly TextBox BufferFindQuery = new()
    {
        AcceptsReturn = false, IsUndoEnabled = false, ContextMenu = null, Width = 240,
        PlaceholderText = "Case-sensitive literal in displayed buffer",
    };
    internal readonly Button BufferFindFirst = new() { Content = "Find first" };
    internal readonly Button BufferFindNext = new() { Content = "Find next", IsEnabled = false };
    internal readonly Button BufferFindPrevious = new() { Content = "Find previous", IsEnabled = false };
    internal readonly Button BufferFindClear = new() { Content = "Clear Find" };
    internal readonly NamedTextBlock BufferFindOutcome = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly DispatcherTimer bufferFindTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Func<Func<bool>?>? captureBufferFind;
    private Func<bool>? bufferFindCurrent;
    private SnapshotLiteralSpan? bufferFindCursor;
    private SnapshotLiteralSpan? bufferFindSelection;
    private string? bufferFindSelectionText;
    private long bufferFindGeneration;
    private bool applyingBufferFindSelection;
    private bool bufferFindDisposed;

    private StackPanel CreateBufferFind()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = "Displayed owned buffer Find (not metadata): case-sensitive literal, complete nonoverlapping count, no wrap. "
                + "Ctrl+F query; Enter first; F3 next; Shift+F3 previous; Escape in Find clears and returns to the source.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(Source, "Exact displayed Kora-owned instruction or prompt source editor; unsaved edits retained");
        AutomationProperties.SetName(BufferFindQuery, $"Displayed owned buffer literal Find query; one line; maximum "
            + $"{LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 characters / {LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes; no clipboard transfer");
        AutomationProperties.SetName(BufferFindOutcome, "Displayed owned buffer Find complete count, ordinal and exact UTF-16 location");
        toolbar.Children.Add(BufferFindQuery);
        foreach (var button in new[] { BufferFindFirst, BufferFindNext, BufferFindPrevious, BufferFindClear })
        {
            button.Margin = new(3);
            AutomationProperties.SetName(button, "Displayed owned buffer " + button.Content);
            toolbar.Children.Add(button);
            button.DetachedFromVisualTree += OnBufferFindDetached;
        }
        panel.Children.Add(toolbar);
        panel.Children.Add(BufferFindOutcome);
        BufferFindFirst.Click += OnBufferFindFirst;
        BufferFindNext.Click += OnBufferFindNext;
        BufferFindPrevious.Click += OnBufferFindPrevious;
        BufferFindClear.Click += OnBufferFindClear;
        BufferFindQuery.PropertyChanged += OnBufferFindQueryChanged;
        BufferFindQuery.AddHandler(TextBox.CopyingToClipboardEvent, BlockBufferFindTransfer);
        BufferFindQuery.AddHandler(TextBox.CuttingToClipboardEvent, BlockBufferFindTransfer);
        BufferFindQuery.AddHandler(TextBox.PastingFromClipboardEvent, BlockBufferFindTransfer);
        BufferFindQuery.AddHandler(ContextRequestedEvent, BlockBufferFindContext, RoutingStrategies.Tunnel);
        BufferFindQuery.AddHandler(KeyDownEvent, BlockBufferFindTransferKeys, RoutingStrategies.Tunnel);
        Source.PropertyChanged += OnBufferFindSourceChanged;
        NameInput.PropertyChanged += OnBufferFindIdentityChanged;
        KindInput.SelectionChanged += OnBufferFindIdentitySelectionChanged;
        Files.SelectionChanged += OnBufferFindIdentitySelectionChanged;
        Source.DetachedFromVisualTree += OnBufferFindDetached;
        BufferFindQuery.DetachedFromVisualTree += OnBufferFindDetached;
        BufferFindOutcome.DetachedFromVisualTree += OnBufferFindDetached;
        AddHandler(KeyDownEvent, OnBufferFindKeyDown, RoutingStrategies.Tunnel);
        bufferFindTimer.Tick += OnBufferFindTick;
        Closing += (_, _) => RetireBufferFind(clearQuery: true);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible) { RetireBufferFind(clearQuery: true); }
        };
        Closed += (_, _) => DisposeBufferFind();
        return panel;
    }

    internal void BindBufferFind(Func<Func<bool>?> capture) => captureBufferFind = capture;

    private void OnBufferFindTick(object? sender, EventArgs args)
    {
        if (bufferFindCurrent?.Invoke() != true) { RetireBufferFind(clearQuery: true); }
    }

    private void OnBufferFindQueryChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty) { RetireBufferFind(); }
    }

    private void OnBufferFindSourceChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty) { RetireBufferFind(); }
        else if (!applyingBufferFindSelection && bufferFindSelection is not null
            && (args.Property == TextBox.SelectionStartProperty || args.Property == TextBox.SelectionEndProperty))
        {
            // A user's selection is not Find-owned and must not be collapsed by retirement.
            bufferFindSelection = null;
            bufferFindSelectionText = null;
            RetireBufferFind();
        }
    }

    private void OnBufferFindIdentityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty) { RetireBufferFind(clearQuery: true); }
    }

    private void OnBufferFindIdentitySelectionChanged(object? sender, SelectionChangedEventArgs args) =>
        RetireBufferFind(clearQuery: true);
    private void OnBufferFindDetached(object? sender, VisualTreeAttachmentEventArgs args) => DisposeBufferFind();
    private void OnBufferFindFirst(object? sender, RoutedEventArgs args) => FindBuffer(SnapshotFindDirection.First);
    private void OnBufferFindNext(object? sender, RoutedEventArgs args) => FindBuffer(SnapshotFindDirection.Next);
    private void OnBufferFindPrevious(object? sender, RoutedEventArgs args) => FindBuffer(SnapshotFindDirection.Previous);
    private void OnBufferFindClear(object? sender, RoutedEventArgs args) => RetireBufferFind(clearQuery: true);

    private void OnBufferFindKeyDown(object? sender, KeyEventArgs args)
    {
        var findControl = ReferenceEquals(args.Source, BufferFindQuery) || ReferenceEquals(args.Source, BufferFindFirst)
            || ReferenceEquals(args.Source, BufferFindNext) || ReferenceEquals(args.Source, BufferFindPrevious)
            || ReferenceEquals(args.Source, BufferFindClear);
        if (args.Key == Key.Escape && findControl)
        {
            args.Handled = true;
            RetireBufferFind(clearQuery: true);
            var admission = captureBufferFind?.Invoke();
            if (admission is null) { return; }
            var generation = bufferFindGeneration;
            var focused = Source.Focus();
            if (generation != bufferFindGeneration) { return; }
            if (!admission()) { RetireBufferFind(clearQuery: true); return; }
            if (!focused) { BufferFindOutcome.Text = "Source focus unavailable; use Tab to reach the native source. Find cleared; draft retained."; }
            return;
        }
        if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
        {
            args.Handled = true;
            if (!EnsureBufferFind()) { return; }
            var generation = bufferFindGeneration;
            var focused = BufferFindQuery.Focus();
            if (generation != bufferFindGeneration) { return; }
            if (bufferFindCurrent?.Invoke() != true) { RetireBufferFind(clearQuery: true); return; }
            if (!focused)
            { RetireBufferFind(); BufferFindOutcome.Text = "Find query focus unavailable; use Tab to reach the native query and retry."; }
            return;
        }
        if (args.Key == Key.F3 && args.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            args.Handled = true;
            FindBuffer(args.KeyModifiers == KeyModifiers.Shift ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
        }
        else if (args.Key == Key.Enter && ReferenceEquals(args.Source, BufferFindQuery) && args.KeyModifiers == KeyModifiers.None)
        { args.Handled = true; FindBuffer(SnapshotFindDirection.First); }
    }

    private bool EnsureBufferFind()
    {
        if (bufferFindDisposed) { return false; }
        if (bufferFindCurrent is not null && bufferFindCurrent()) { return true; }
        RetireBufferFind(clearQuery: bufferFindCurrent is not null);
        bufferFindCurrent = captureBufferFind?.Invoke();
        if (bufferFindCurrent is null)
        {
            RetireBufferFind(clearQuery: true);
            BufferFindOutcome.Text = "Find unavailable: explicitly Inspect or Create the exact owned kind and ID in the current private owning editor. Draft retained.";
            return false;
        }
        bufferFindTimer.Start();
        return true;
    }

    private void FindBuffer(SnapshotFindDirection direction)
    {
        if (!EnsureBufferFind()) { return; }
        var text = Source.Text ?? string.Empty;
        var query = BufferFindQuery.Text ?? string.Empty;
        var cursor = bufferFindCursor;
        var generation = ++bufferFindGeneration;
        bool Current()
        {
            if (bufferFindCurrent?.Invoke() == true && generation == bufferFindGeneration && !bufferFindDisposed) { return true; }
            if (generation == bufferFindGeneration) { RetireBufferFind(clearQuery: true); }
            return false;
        }
        ReleaseBufferFindSelection();
        if (!Current()) { return; }
        BufferFindOutcome.Text = null;
        if (!Current()) { return; }
        BufferFindNext.IsEnabled = BufferFindPrevious.IsEnabled = false;
        if (!Current()) { return; }
        if (!SnapshotLiteralFind.IsValidQuery(query))
        {
            bufferFindCursor = null;
            BufferFindOutcome.Text = "Invalid literal Find query: enter nonempty, bounded, well-formed one-line text without unsafe controls. Nothing was truncated.";
            return;
        }
        if (direction != SnapshotFindDirection.First && cursor is null)
        {
            BufferFindOutcome.Text = "Find first after editing or clearing; no current match to navigate.";
            return;
        }
        var found = SnapshotLiteralFind.FindCounted(text, query, direction, cursor);
        if (!Current()) { return; }
        bufferFindCursor = found.Match ?? (direction == SnapshotFindDirection.First ? null : cursor);
        BufferFindNext.IsEnabled = BufferFindPrevious.IsEnabled = bufferFindCursor is not null;
        if (!Current()) { return; }
        BufferFindOutcome.Text = found.Match is { } match
            ? $"Match {found.Ordinal} of {found.Count}; UTF-16 [{match.Start}, {match.Start + match.Length}); end exclusive."
            : found.Count == 0 ? "0 matches in this exact displayed owned buffer."
                : $"No wrap; {(direction == SnapshotFindDirection.Next ? "forward" : "backward")} edge after match {found.Ordinal} of {found.Count}. No match selected.";
        if (!Current()) { return; }
        if (found.Match is not { } span) { return; }
        bufferFindSelection = span;
        bufferFindSelectionText = text;
        applyingBufferFindSelection = true;
        try
        {
            Source.CaretIndex = span.Start + span.Length;
            if (!Current()) { return; }
            Source.ClearSelection();
            if (!Current()) { return; }
            Source.SelectionStart = span.Start;
            if (!Current()) { return; }
            Source.SelectionEnd = span.Start + span.Length;
            if (!Current()) { return; }
            var focused = Source.Focus();
            if (!Current()) { return; }
            if (!focused || Source.SelectionStart != span.Start || Source.SelectionEnd != span.Start + span.Length)
            {
                RetireBufferFind();
                BufferFindOutcome.Text = "Exact buffer match focus/selection unavailable; no match or count claimed. Use Tab to reach the source and Find first to retry.";
                return;
            }
        }
        finally { applyingBufferFindSelection = false; }
    }

    private void ReleaseBufferFindSelection()
    {
        var span = bufferFindSelection;
        var text = bufferFindSelectionText;
        bufferFindSelection = null;
        bufferFindSelectionText = null;
        if (span is not null && string.Equals(Source.Text, text, StringComparison.Ordinal)
            && Source.SelectionStart == span.Start && Source.SelectionEnd == span.Start + span.Length)
        { Source.ClearSelection(); }
    }

    internal void RetireBufferFind(bool clearQuery = false)
    {
        bufferFindGeneration++;
        bufferFindCurrent = null;
        bufferFindCursor = null;
        bufferFindTimer.Stop();
        ReleaseBufferFindSelection();
        BufferFindOutcome.Text = null;
        BufferFindNext.IsEnabled = BufferFindPrevious.IsEnabled = false;
        if (clearQuery) { BufferFindQuery.Text = string.Empty; }
    }

    private static void BlockBufferFindTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
    private static void BlockBufferFindContext(object? sender, ContextRequestedEventArgs args) => args.Handled = true;
    private static void BlockBufferFindTransferKeys(object? sender, KeyEventArgs args)
    {
        var control = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != KeyModifiers.None;
        var shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (control && args.Key is Key.C or Key.X or Key.V or Key.Insert
            || shift && args.Key is Key.Insert or Key.Delete or Key.F10 || args.Key == Key.Apps)
        { args.Handled = true; }
    }

    private void DisposeBufferFind()
    {
        if (bufferFindDisposed) { return; }
        bufferFindDisposed = true;
        captureBufferFind = null;
        RetireBufferFind(clearQuery: true);
        bufferFindTimer.Tick -= OnBufferFindTick;
        BufferFindFirst.Click -= OnBufferFindFirst;
        BufferFindNext.Click -= OnBufferFindNext;
        BufferFindPrevious.Click -= OnBufferFindPrevious;
        BufferFindClear.Click -= OnBufferFindClear;
        BufferFindQuery.PropertyChanged -= OnBufferFindQueryChanged;
        Source.PropertyChanged -= OnBufferFindSourceChanged;
        NameInput.PropertyChanged -= OnBufferFindIdentityChanged;
        KindInput.SelectionChanged -= OnBufferFindIdentitySelectionChanged;
        Files.SelectionChanged -= OnBufferFindIdentitySelectionChanged;
        Source.DetachedFromVisualTree -= OnBufferFindDetached;
        BufferFindQuery.DetachedFromVisualTree -= OnBufferFindDetached;
        BufferFindOutcome.DetachedFromVisualTree -= OnBufferFindDetached;
        foreach (var button in new[] { BufferFindFirst, BufferFindNext, BufferFindPrevious, BufferFindClear })
        { button.DetachedFromVisualTree -= OnBufferFindDetached; }
        RemoveHandler(KeyDownEvent, OnBufferFindKeyDown);
        BufferFindQuery.IsEnabled = BufferFindFirst.IsEnabled = BufferFindClear.IsEnabled = false;
    }
}
