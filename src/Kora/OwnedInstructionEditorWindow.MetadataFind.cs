using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

using Kora.Application.Definitions;
using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class OwnedInstructionEditorWindow
{
    internal readonly TextBox MetadataFindQuery = new() { Width = 220, IsUndoEnabled = false };
    internal readonly Button MetadataFindFocus = new() { Content = "Find in selected metadata", Margin = new(3) };
    internal readonly Button MetadataFindFirst = new() { Content = "First", Margin = new(3) };
    internal readonly Button MetadataFindNext = new() { Content = "Next match", Margin = new(3) };
    internal readonly Button MetadataFindPrevious = new() { Content = "Previous match", Margin = new(3) };
    internal readonly Button MetadataFindClear = new() { Content = "Clear Find", Margin = new(3) };
    internal readonly NamedTextBlock MetadataFindMatch = new() { Focusable = true };
    internal readonly NamedTextBlock MetadataFindStatus = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private AdmittedMetadataFindState? metadataFind;
    private bool renderingMetadataFind;
    private long metadataFindRevision;
    private readonly StackPanel metadataFindPanel = new();

    private StackPanel BuildMetadataFind()
    {
        var panel = metadataFindPanel;
        panel.Children.Add(new TextBlock
        {
            Text = "Find only in the selected displayed owned inventory metadata details, not source or draft. "
                + "Case/space-sensitive one-line literal; complete nonoverlapping count for these details only; no wrap. "
                + "No freshness, integrity, read, trust or use authority.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { MetadataFindFocus, MetadataFindQuery, MetadataFindFirst,
            MetadataFindNext, MetadataFindPrevious, MetadataFindClear })
        { controls.Children.Add(control); }
        panel.Children.Add(controls);
        panel.Children.Add(MetadataFindStatus);
        panel.Children.Add(new ScrollViewer
        {
            Content = MetadataFindMatch, MaxHeight = 80,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        AutomationProperties.SetName(MetadataFindFocus, "Focus private selected metadata details Find query; not source Find");
        AutomationProperties.SetName(MetadataFindQuery, "Private selected metadata details Find query; one line, at most 256 UTF-16 units and 512 UTF-8 bytes; Enter finds first");
        AutomationProperties.SetName(MetadataFindMatch, "One exact inert selected metadata details match; no copy or use");
        AutomationProperties.SetName(MetadataFindStatus, "Selected metadata details Find outcome, count and ordinal only");
        AutomationProperties.SetName(MetadataFindFirst, "Find first selected metadata details match");
        AutomationProperties.SetName(MetadataFindNext, "Find next selected metadata details match; F3; no wrap");
        AutomationProperties.SetName(MetadataFindPrevious, "Find previous selected metadata details match; Shift F3; no wrap");
        AutomationProperties.SetName(MetadataFindClear, "Clear private metadata details Find; Escape");
        MetadataFindQuery.ContextMenu = null;
        MetadataFindQuery.ContextFlyout = null;
        MetadataFindQuery.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsEnabledProperty && metadataFind is not null && !MetadataFindQuery.IsEnabled)
            { RetireMetadataFind(); return; }
            if (change.Property != TextBox.TextProperty
                || renderingMetadataFind && string.Equals(MetadataFindQuery.Text ?? string.Empty, metadataFind?.Query ?? string.Empty, StringComparison.Ordinal))
            { return; }
            metadataFindRevision++;
            metadataFind?.Edit(MetadataFindQuery.Text ?? string.Empty);
            RenderMetadataFind();
        };
        MetadataFindFocus.Click += (_, _) =>
        {
            var admitted = metadataFind;
            var revision = metadataFindRevision;
            RenderMetadataFind();
            if (admitted is null || !ReferenceEquals(metadataFind, admitted) || revision != metadataFindRevision) { return; }
            var focused = MetadataFindQuery.Focus() && MetadataFindQuery.IsFocused;
            if (ReferenceEquals(metadataFind, admitted) && revision == metadataFindRevision && !focused)
            { admitted.ReportFocusUnavailable(); }
            RenderMetadataFind();
        };
        MetadataFindFirst.Click += (_, _) => FindMetadata(SnapshotFindDirection.First);
        MetadataFindNext.Click += (_, _) => FindMetadata(SnapshotFindDirection.Next);
        MetadataFindPrevious.Click += (_, _) => FindMetadata(SnapshotFindDirection.Previous);
        MetadataFindClear.Click += (_, _) => ClearMetadataFindQuery();
        panel.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape) { ClearMetadataFindQuery(); args.Handled = true; }
            else if (args.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift
                && (args.Key == Key.F3 || args.Key == Key.Enter && MetadataFindQuery.IsFocused))
            {
                FindMetadata(args.Key == Key.Enter ? SnapshotFindDirection.First
                    : args.KeyModifiers == KeyModifiers.Shift ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
                args.Handled = true;
            }
            else if (args.KeyModifiers.HasFlag(KeyModifiers.Control)
                || args.KeyModifiers.HasFlag(KeyModifiers.Alt)
                || args.KeyModifiers.HasFlag(KeyModifiers.Meta)
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key is Key.Insert or Key.Delete or Key.F10
                || args.Key == Key.Apps)
            { args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        panel.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        panel.AddHandler(TextBox.CopyingToClipboardEvent, BlockMetadataTransfer, RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        panel.AddHandler(TextBox.CuttingToClipboardEvent, BlockMetadataTransfer, RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        panel.AddHandler(TextBox.PastingFromClipboardEvent, BlockMetadataTransfer, RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        Closing += (_, _) => RetireMetadataFind();
        Closed += (_, _) => RetireMetadataFind();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && !IsVisible || change.Property == IsEnabledProperty && !IsEnabled)
            { RetireMetadataFind(); }
        };
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        EventHandler tick = (_, _) => RenderMetadataFind();
        timer.Tick += tick;
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => { timer.Stop(); timer.Tick -= tick; };
        RenderMetadataFind();
        return panel;
    }

    internal void AdmitMetadataFind(OwnedDefinitionInventoryRow row, Func<bool> current)
    {
        RetireMetadataFind();
        // This is a typed inventory-details surface, never the source editor or a generic inspection result.
        if (!current() || !IsVisible || !IsEnabled || !ReferenceEquals(Files.SelectedItem, row)) { return; }
        var details = inventoryDetails.Text ?? string.Empty;
        metadataFind = new(details, () => IsVisible && IsEnabled && ReferenceEquals(Files.SelectedItem, row)
            && string.Equals(inventoryDetails.Text, details, StringComparison.Ordinal) && current(), counted: true);
        RenderMetadataFind();
    }

    internal void RetireMetadataFind()
    {
        metadataFindRevision++;
        metadataFind?.Dispose();
        metadataFind = null;
        RenderMetadataFind();
    }

    private void FindMetadata(SnapshotFindDirection direction)
    {
        metadataFindRevision++;
        var admitted = metadataFind;
        var revision = metadataFindRevision;
        admitted?.Find(direction);
        RenderMetadataFind();
        bool Current() => admitted is not null && ReferenceEquals(metadataFind, admitted) && revision == metadataFindRevision
            && admitted.CheckAdmission() && admitted.Match is not null;
        if (!Current()) { return; }
        UpdateLayout();
        if (!Current()) { RenderMetadataFind(); return; }
        MetadataFindMatch.BringIntoView();
        if (!Current()) { RenderMetadataFind(); return; }
        var focused = MetadataFindMatch.Focus() && MetadataFindMatch.IsFocused;
        if (Current() && !focused) { admitted!.ReportFocusUnavailable(); }
        RenderMetadataFind();
    }

    private void RenderMetadataFind()
    {
        var wasRendering = renderingMetadataFind;
        renderingMetadataFind = true;
        try
        {
            var admitted = metadataFind;
            var revision = metadataFindRevision;
            bool Current() => ReferenceEquals(metadataFind, admitted) && revision == metadataFindRevision;
            var available = admitted?.CheckAdmission() == true;
            if (!Current()) { return; }
            if (!available && admitted is not null) { RetireMetadataFind(); return; }
            var hasMatch = admitted?.Match is not null;
            if ((!available && MetadataFindQuery.IsFocused) || (!hasMatch && MetadataFindMatch.IsFocused))
            { FocusManager?.Focus(null); }
            if (!Current()) { return; }
            MetadataFindQuery.Text = admitted?.Query ?? string.Empty;
            if (!Current()) { return; }
            MetadataFindQuery.IsEnabled = available;
            if (!Current()) { return; }
            MetadataFindFocus.IsEnabled = available;
            if (!Current()) { return; }
            MetadataFindFirst.IsEnabled = available;
            if (!Current()) { return; }
            MetadataFindClear.IsEnabled = available;
            if (!Current()) { return; }
            MetadataFindNext.IsEnabled = admitted?.CanNavigate == true;
            if (!Current()) { return; }
            MetadataFindPrevious.IsEnabled = admitted?.CanNavigate == true;
            if (!Current()) { return; }
            MetadataFindStatus.Text = admitted?.Status ?? string.Empty;
            if (!Current()) { return; }
            MetadataFindMatch.Text = admitted?.ExactMatch ?? string.Empty;
            if (!Current()) { return; }
            MetadataFindMatch.IsVisible = hasMatch;
        }
        finally { renderingMetadataFind = wasRendering; }
    }

    private void ClearMetadataFindQuery()
    {
        metadataFindRevision++;
        metadataFind?.Clear();
        RenderMetadataFind();
    }

    private static void BlockMetadataTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
}
