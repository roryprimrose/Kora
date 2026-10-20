using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Core.Context;

namespace Kora;

internal sealed partial class SavedDraftCatalogueWindow
{
    private void InitializeMetadataFind(SavedDraftCatalogueViewModel model)
    {
        MetadataFindFirst.Click += (_, _) => Find(SnapshotFindDirection.First);
        MetadataFindNext.Click += (_, _) => Find(SnapshotFindDirection.Next);
        MetadataFindPrevious.Click += (_, _) => Find(SnapshotFindDirection.Previous);
        MetadataFindClear.Click += (_, _) => model.ClearMetadataFind();
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
            {
                model.SynchronizeAuthority();
                if (model.CanFindMetadata)
                {
                    var revision = model.MetadataFindRevision;
                    MetadataFindQuery.BringIntoView();
                    model.SynchronizeAuthority();
                    if (model.IsMetadataFindRevisionCurrent(revision) && !MetadataFindQuery.Focus()) { model.MetadataFindFocusUnavailable(); }
                    model.SynchronizeAuthority();
                }
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                model.ClearMetadataFind();
                args.Handled = true;
            }
            else if (args.Key == Key.F3 || args.Key == Key.Enter && MetadataFindQuery.IsFocused)
            {
                Find(args.Key == Key.Enter ? SnapshotFindDirection.First
                    : args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
                args.Handled = true;
            }
            else if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is Key.C or Key.X or Key.V or Key.Insert
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key is Key.Insert or Key.Delete
                || args.Key == Key.Apps || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.F10)
            { args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.CopyingToClipboardEvent, BlockMetadataTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.CuttingToClipboardEvent, BlockMetadataTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.PastingFromClipboardEvent, BlockMetadataTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);

        void Find(SnapshotFindDirection direction)
        {
            var revision = model.FindMetadata(direction);
            if (!Current()) { return; }
            UpdateLayout();
            if (!Current()) { return; }
            MetadataFindSpan.BringIntoView();
            if (!Current()) { return; }
            if (!MetadataFindSpan.Focus()) { model.MetadataFindFocusUnavailable(); }
            model.SynchronizeAuthority();

            bool Current()
            {
                model.SynchronizeAuthority();
                return model.IsMetadataFindRevisionCurrent(revision) && model.HasMetadataFindMatch;
            }
        }
    }

    private static void BlockMetadataTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
}
