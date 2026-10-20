using System.ComponentModel;
using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class NewRequestWindow
{
    private readonly TextBox draftFindQuery = new() { AcceptsReturn = false, IsUndoEnabled = false };
    private readonly NamedTextBlock draftFindStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button draftFindFirst = new() { Content = "Find first in draft" };
    private readonly Button draftFindNext = new() { Content = "Find next", IsEnabled = false };
    private readonly Button draftFindPrevious = new() { Content = "Find previous", IsEnabled = false };
    private readonly Button draftFindClear = new() { Content = "Clear draft Find" };
    private readonly DispatcherTimer draftFindTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Func<Func<bool>?>? captureDraftFind;
    private Func<bool>? draftFindCurrent;
    private INotifyPropertyChanged? draftFindHost;
    private SnapshotLiteralSpan? draftFindCursor;
    private long draftFindGeneration;
    private long draftFindLeaseGeneration;
    private bool draftFindDisposed;
    internal bool IsObservingDraftFind => draftFindTimer.IsEnabled;

    internal void BindDraftFind(Func<Func<bool>?> capture, INotifyPropertyChanged host)
    {
        captureDraftFind = capture;
        draftFindHost = host;
        draftFindQuery.Name = "DraftFindQuery";
        draftFindStatus.Name = "DraftFindStatus";
        draftFindFirst.Name = "DraftFindFirst";
        draftFindNext.Name = "DraftFindNext";
        draftFindPrevious.Name = "DraftFindPrevious";
        draftFindClear.Name = "DraftFindClear";
        draftFindQuery.PlaceholderText = $"One-line literal: {LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 / "
            + $"{LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes maximum";
        AutomationProperties.SetName(draftFindQuery, "Find in current memory DRAFT BODY only; case-sensitive one-line literal");
        AutomationProperties.SetName(draftFindStatus, "Draft-only Find total, ordinal and focus outcome; not an answer or approval");
        foreach (var button in new[] { draftFindFirst, draftFindNext, draftFindPrevious, draftFindClear })
        { AutomationProperties.SetName(button, button.Content + "; current draft only; no wrap"); }
        DraftFindPanel.Children.Add(new NamedTextBlock
        {
            Text = "Find in current memory DRAFT BODY only, including invalid or unsaved edits. Not reviewed body, "
                + "saved text, assistant answer or hidden sources. Case-sensitive literal; exact spaces; nonoverlapping; no wrap. "
                + "Ctrl+F query; Enter first; F3 next; Shift+F3 previous. Find does not edit, save, review or submit.",
            TextWrapping = TextWrapping.Wrap,
        });
        DraftFindPanel.Children.Add(draftFindQuery);
        var actions = new WrapPanel();
        foreach (var button in new[] { draftFindFirst, draftFindNext, draftFindPrevious, draftFindClear })
        { actions.Children.Add(button); }
        DraftFindPanel.Children.Add(actions);
        DraftFindPanel.Children.Add(draftFindStatus);
        draftFindFirst.Click += DraftFindFirst;
        draftFindNext.Click += DraftFindNext;
        draftFindPrevious.Click += DraftFindPrevious;
        draftFindClear.Click += DraftFindClear;
        draftFindQuery.PropertyChanged += DraftFindQueryChanged;
        NewRequestBody.PropertyChanged += DraftFindSourceChanged;
        NewRequestBody.DetachedFromVisualTree += DraftFindDetached;
        draftFindQuery.DetachedFromVisualTree += DraftFindDetached;
        PropertyChanged += DraftFindWindowChanged;
        model.Composer.PropertyChanged += DraftFindContextChanged;
        host.PropertyChanged += DraftFindContextChanged;
        Closing += DraftFindClosing;
        draftFindTimer.Tick += DraftFindTick;
        AddHandler(KeyDownEvent, DraftFindKeyDown, RoutingStrategies.Tunnel);
    }

    private bool DraftFindAvailable() => !draftFindDisposed && !privateClose && !choosing
        && IsVisible && ReferenceEquals(DataContext, model) && model.CanEdit && !model.Composer.Busy
        && ReferenceEquals(TopLevel.GetTopLevel(NewRequestBody), this) && ReferenceEquals(TopLevel.GetTopLevel(draftFindQuery), this)
        && ReferenceEquals(NewRequestBody.DataContext, model) && ReferenceEquals(draftFindQuery.DataContext, model)
        && NewRequestBody.IsEffectivelyVisible && NewRequestBody.IsEffectivelyEnabled && !NewRequestBody.IsReadOnly
        && draftFindQuery.IsEffectivelyVisible && draftFindQuery.IsEffectivelyEnabled
        && string.Equals(NewRequestBody.Text ?? string.Empty, model.Body, StringComparison.Ordinal);

    private void DraftFindFirst(object? sender, RoutedEventArgs args) => FindDraft(SnapshotFindDirection.First);
    private void DraftFindNext(object? sender, RoutedEventArgs args) => FindDraft(SnapshotFindDirection.Next);
    private void DraftFindPrevious(object? sender, RoutedEventArgs args) => FindDraft(SnapshotFindDirection.Previous);
    private void DraftFindClear(object? sender, RoutedEventArgs args) => RetireDraftFind();
    private void DraftFindContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        Interlocked.Increment(ref draftFindGeneration);
        if (Dispatcher.UIThread.CheckAccess()) { RetireDraftFind(); }
        else { Dispatcher.UIThread.Post(() => { if (!draftFindDisposed) { RetireDraftFind(); } }); }
    }
    private void DraftFindClosing(object? sender, WindowClosingEventArgs args) => RetireDraftFind();
    private void DraftFindDetached(object? sender, VisualTreeAttachmentEventArgs args) => DisposeDraftFind();
    private void DraftFindTick(object? sender, EventArgs args) => ObserveDraftFindLifetime();
    internal void ObserveDraftFindLifetime()
    {
        if (!DraftFindAvailable() || draftFindLeaseGeneration != Interlocked.Read(ref draftFindGeneration)
            || draftFindCurrent?.Invoke() != true) { RetireDraftFind(); }
    }
    private void DraftFindQueryChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != TextBox.TextProperty) { return; }
        var generation = RetireDraftFind(clearQuery: false);
        if (string.IsNullOrEmpty(draftFindQuery.Text) || !DraftFindAvailable()) { return; }
        var guard = captureDraftFind?.Invoke();
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return; }
        if (guard?.Invoke() != true) { RetireDraftFind(); return; }
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return; }
        draftFindCurrent = guard;
        draftFindLeaseGeneration = generation;
        draftFindTimer.Start();
    }
    private void DraftFindSourceChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty || args.Property == IsEnabledProperty
            || args.Property == IsVisibleProperty || args.Property == TextBox.IsReadOnlyProperty)
        { RetireDraftFind(); }
    }
    private void DraftFindWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == DataContextProperty || args.Property == IsVisibleProperty) { RetireDraftFind(); }
    }

    private long RetireDraftFind(bool clearQuery = true)
    {
        var generation = Interlocked.Increment(ref draftFindGeneration);
        draftFindCurrent = null;
        draftFindCursor = null;
        draftFindTimer.Stop();
        draftFindNext.IsEnabled = false;
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return generation; }
        draftFindPrevious.IsEnabled = false;
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return generation; }
        draftFindStatus.Text = string.Empty;
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return generation; }
        if (clearQuery) { draftFindQuery.Text = string.Empty; }
        // Never collapse or restore native selection: it may now belong to the user's edit.
        return generation;
    }

    private void FindDraft(SnapshotFindDirection direction)
    {
        if (draftFindDisposed) { return; }
        var initialGeneration = Interlocked.Read(ref draftFindGeneration);
        var cursor = draftFindCursor;
        var previous = draftFindCurrent;
        var previousCurrent = draftFindLeaseGeneration == Interlocked.Read(ref draftFindGeneration)
            && previous?.Invoke() == true;
        if (initialGeneration != Interlocked.Read(ref draftFindGeneration)) { return; }
        var literal = draftFindQuery.Text ?? string.Empty;
        var generation = RetireDraftFind(clearQuery: false);
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return; }
        var guard = direction == SnapshotFindDirection.First ? captureDraftFind?.Invoke() : previousCurrent ? previous : null;
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return; }
        if (!DraftFindAvailable() || guard?.Invoke() != true)
        { RetireDraftFind(); draftFindStatus.Text = "Draft Find unavailable or stale; no selection or focus claimed."; return; }
        if (generation != Interlocked.Read(ref draftFindGeneration)) { return; }
        draftFindCurrent = guard;
        draftFindLeaseGeneration = generation;
        draftFindTimer.Start();
        if (!SnapshotLiteralFind.IsValidQuery(literal))
        {
            draftFindStatus.Text = "Invalid literal: enter nonempty, bounded, well-formed one-line text without unsafe controls. Nothing truncated.";
            return;
        }
        if (direction != SnapshotFindDirection.First && cursor is null)
        { draftFindStatus.Text = "Find first after editing or clearing; no current draft match to navigate."; return; }
        var text = NewRequestBody.Text ?? string.Empty;
        SnapshotLiteralSpan? selectionToVerify = null;
        bool Current() => generation == Interlocked.Read(ref draftFindGeneration) && DraftFindAvailable() && guard()
            && string.Equals(NewRequestBody.Text ?? string.Empty, text, StringComparison.Ordinal)
            && string.Equals(draftFindQuery.Text, literal, StringComparison.Ordinal)
            && (selectionToVerify is null || NewRequestBody.IsFocused && NewRequestBody.SelectionStart == selectionToVerify.Start
                && NewRequestBody.SelectionEnd == selectionToVerify.Start + selectionToVerify.Length)
            && generation == Interlocked.Read(ref draftFindGeneration);
        bool Check()
        {
            if (Current()) { return true; }
            if (generation == Interlocked.Read(ref draftFindGeneration)) { RetireDraftFind(); }
            return false;
        }
        var result = SnapshotLiteralFind.FindCounted(text, literal, direction, cursor);
        if (!Check()) { return; }
        if (result.Match is { } match)
        {
            NewRequestBody.SelectionStart = match.Start;
            if (!Check()) { return; }
            NewRequestBody.SelectionEnd = match.Start + match.Length;
            if (!Check()) { return; }
            NewRequestBody.BringIntoView();
            if (!Check()) { return; }
            var focused = NewRequestBody.Focus();
            if (!Check()) { return; }
            if (!focused || !NewRequestBody.IsFocused || NewRequestBody.SelectionStart != match.Start
                || NewRequestBody.SelectionEnd != match.Start + match.Length)
            {
                RetireDraftFind();
                draftFindStatus.Text = "Native draft focus/selection unavailable; no match claimed. Use the editable draft directly.";
                return;
            }
            selectionToVerify = match;
        }
        draftFindCursor = result.Match ?? (direction == SnapshotFindDirection.First ? null : cursor);
        draftFindNext.IsEnabled = draftFindCursor is not null;
        if (!Check()) { return; }
        draftFindPrevious.IsEnabled = draftFindCursor is not null;
        if (!Check()) { return; }
        draftFindStatus.Text = string.Create(CultureInfo.InvariantCulture,
            $"Current draft only: total {result.Count}; ordinal {result.Ordinal}. ")
            + (result.Match is not null ? "Exact editable-source selection focused; case-sensitive; nonoverlapping; no wrap."
                : direction == SnapshotFindDirection.First ? "No literal match; no selection or focus claimed."
                : "End of direction; no wrap; retained anchor only, no new selection or focus claimed.");
        if (Check()) { draftFindTimer.Start(); }
    }

    private void DraftFindKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
        {
            args.Handled = true;
            var generation = Interlocked.Read(ref draftFindGeneration);
            var guard = captureDraftFind?.Invoke();
            if (generation != Interlocked.Read(ref draftFindGeneration)) { return; }
            if (!DraftFindAvailable() || guard?.Invoke() != true)
            { RetireDraftFind(); draftFindStatus.Text = "Draft Find unavailable; no focus claimed."; return; }
            if (generation != Interlocked.Read(ref draftFindGeneration)) { return; }
            var focused = draftFindQuery.Focus();
            if (generation != Interlocked.Read(ref draftFindGeneration) || !DraftFindAvailable() || !guard())
            { RetireDraftFind(); return; }
            if (!focused || !draftFindQuery.IsFocused)
            { draftFindStatus.Text = "Native Find query focus unavailable; use the labeled query directly."; }
        }
        else if (args.Key == Key.Enter && draftFindQuery.IsFocused && args.KeyModifiers == KeyModifiers.None)
        { args.Handled = true; FindDraft(SnapshotFindDirection.First); }
        else if (args.Key == Key.F3 && args.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        { args.Handled = true; FindDraft(args.KeyModifiers == KeyModifiers.Shift ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next); }
    }

    private void DisposeDraftFind()
    {
        if (draftFindDisposed) { return; }
        draftFindDisposed = true;
        RetireDraftFind();
        captureDraftFind = null;
        draftFindTimer.Tick -= DraftFindTick;
        draftFindFirst.Click -= DraftFindFirst;
        draftFindNext.Click -= DraftFindNext;
        draftFindPrevious.Click -= DraftFindPrevious;
        draftFindClear.Click -= DraftFindClear;
        draftFindQuery.PropertyChanged -= DraftFindQueryChanged;
        NewRequestBody.PropertyChanged -= DraftFindSourceChanged;
        NewRequestBody.DetachedFromVisualTree -= DraftFindDetached;
        draftFindQuery.DetachedFromVisualTree -= DraftFindDetached;
        PropertyChanged -= DraftFindWindowChanged;
        model.Composer.PropertyChanged -= DraftFindContextChanged;
        if (draftFindHost is not null) { draftFindHost.PropertyChanged -= DraftFindContextChanged; }
        draftFindHost = null;
        Closing -= DraftFindClosing;
        RemoveHandler(KeyDownEvent, DraftFindKeyDown);
        DraftFindPanel.IsEnabled = false;
    }
}
