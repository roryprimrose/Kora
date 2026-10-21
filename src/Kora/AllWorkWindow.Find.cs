using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Core.Context;

namespace Kora;

internal sealed partial class AllWorkWindow
{
    private bool renderingFind;
    internal Func<bool>? FocusFindMatch { get; set; }
    internal Func<bool>? FocusFindQuery { get; set; }

    private void InitializeFind(AllWorkViewModel model)
    {
        DetailsFindQuery.ContextMenu = null;
        DetailsFindQuery.ContextFlyout = null;
        DetailsFindQuery.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty && !renderingFind)
            { model.EditFind(DetailsFindQuery.Text ?? string.Empty); }
        };
        DetailsFindFirst.Click += (_, _) => Find(SnapshotFindDirection.First);
        DetailsFindNext.Click += (_, _) => Find(SnapshotFindDirection.Next);
        DetailsFindPrevious.Click += (_, _) => Find(SnapshotFindDirection.Previous);
        DetailsFindClear.Click += (_, _) => model.ClearFind();
        model.PropertyChanged += Changed;
        Closed += (_, _) =>
        {
            model.PropertyChanged -= Changed;
            FocusFindMatch = null;
            FocusFindQuery = null;
        };
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
            {
                args.Handled = true;
                if (ValidateDisplayedFind())
                {
                    var version = model.FindVersion;
                    var focused = FocusFindQuery?.Invoke() ?? DetailsFindQuery.Focus();
                    if (!ValidateDisplayedFind()) { return; }
                    if (!focused || !DetailsFindQuery.IsFocused) { model.ReportFindQueryFocusUnavailable(version); }
                }
            }
            else if (InFind(args.Source) && args.Key == Key.Escape)
            { model.ClearFind(); args.Handled = true; }
            else if (args.Key == Key.F3 || args.Key == Key.Enter && DetailsFindQuery.IsFocused)
            {
                args.Handled = true;
                Find(args.Key == Key.Enter ? SnapshotFindDirection.First
                    : args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
            }
            else if (InFind(args.Source) && (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is Key.C or Key.X or Key.V or Key.Insert
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key is Key.Insert or Key.Delete
                || args.Key == Key.Apps || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.F10))
            { args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        DetailsFindQuery.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        DetailsFindQuery.AddHandler(TextBox.CopyingToClipboardEvent, BlockFindTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        DetailsFindQuery.AddHandler(TextBox.CuttingToClipboardEvent, BlockFindTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        DetailsFindQuery.AddHandler(TextBox.PastingFromClipboardEvent, BlockFindTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        RenderFind();

        bool InFind(object? source) => ReferenceEquals(source, DetailsFindQuery) || ReferenceEquals(source, DetailsFindMatch)
            || ReferenceEquals(source, DetailsFindFirst) || ReferenceEquals(source, DetailsFindNext)
            || ReferenceEquals(source, DetailsFindPrevious) || ReferenceEquals(source, DetailsFindClear);

        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(AllWorkViewModel.CanFind) or nameof(AllWorkViewModel.FindQuery)
                or nameof(AllWorkViewModel.FindMatch) or nameof(AllWorkViewModel.CanFindNavigate)) { RenderFind(); }
        }

        void RenderFind()
        {
            if (renderingFind) { return; }
            renderingFind = true;
            try
            {
                if ((!model.CanFind && DetailsFindQuery.IsFocused) || (model.FindMatch.Length == 0 && DetailsFindMatch.IsFocused))
                { FocusManager?.Focus(null); }
                DetailsFindQuery.Text = model.FindQuery;
                DetailsFindQuery.IsEnabled = model.CanFind;
                DetailsFindFirst.IsEnabled = model.CanFind;
                DetailsFindClear.IsEnabled = model.CanFind;
                DetailsFindNext.IsEnabled = model.CanFindNavigate;
                DetailsFindPrevious.IsEnabled = model.CanFindNavigate;
                DetailsFindMatch.IsVisible = model.FindMatch.Length != 0;
            }
            finally { renderingFind = false; }
        }

        void Find(SnapshotFindDirection direction)
        {
            if (!ValidateDisplayedFind()) { return; }
            var version = model.FindMetadata(direction);
            if (!CurrentMatch(version)) { return; }
            UpdateLayout();
            if (!CurrentMatch(version)) { return; }
            DetailsFindMatch.BringIntoView();
            if (!CurrentMatch(version)) { return; }
            var focused = FocusFindMatch?.Invoke() ?? DetailsFindMatch.Focus();
            if (!CurrentMatch(version)) { return; }
            if (!focused || !DetailsFindMatch.IsFocused) { model.ReportFindFocusUnavailable(version); }
        }

        bool ValidateDisplayedFind()
        {
            if (string.Equals(SelectedMetadataDetails.Text, model.SelectedDetails, StringComparison.Ordinal))
            { return model.ValidateFind(); }
            model.RetireDisplayedFind();
            return false;
        }

        bool CurrentMatch(long version)
        {
            if (!ValidateDisplayedFind() || !model.HasCurrentFindMatch(version)) { return false; }
            if (string.Equals(DetailsFindMatch.Text, model.FindMatch, StringComparison.Ordinal)) { return true; }
            model.ReportFindFocusUnavailable(version);
            return false;
        }
    }

    private static void BlockFindTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
}
