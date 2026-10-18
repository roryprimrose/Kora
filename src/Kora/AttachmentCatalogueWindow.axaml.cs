using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AttachmentCatalogueWindow : Window
{
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal AttachmentCatalogueWindow(AttachmentCatalogueViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        MetadataFindFirst.Click += (_, _) => FindMetadata(SnapshotFindDirection.First);
        MetadataFindNext.Click += (_, _) => FindMetadata(SnapshotFindDirection.Next);
        MetadataFindPrevious.Click += (_, _) => FindMetadata(SnapshotFindDirection.Previous);
        MetadataFindClear.Click += (_, _) => model.ClearMetadataFind();
        MetadataFindPanel.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                model.ClearMetadataFind();
                args.Handled = true;
            }
            else if (args.Key == Key.F3 || args.Key == Key.Enter && MetadataFindQuery.IsFocused)
            {
                FindMetadata(args.Key == Key.Enter ? SnapshotFindDirection.First
                    : args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
                args.Handled = true;
            }
            else if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is Key.C or Key.X or Key.V or Key.Insert
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key is Key.Insert or Key.Delete
                || args.Key == Key.Apps || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.F10)
            { args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.CopyingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.CuttingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.PastingFromClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        RefreshPage.Click += async (_, _) => await (Operation = model.Refresh());
        NextPage.Click += async (_, _) => await (Operation = model.Refresh(next: true));
        CancelRead.Click += (_, _) => model.Cancel();
        ClearQuery.Click += (_, _) => model.ClearQuery();
        SlotRecords.SelectionChanged += (_, _) => model.Select(SlotRecords.SelectedItem as SessionAttachmentSummary);
        OpenExact.Click += async (_, _) => await (Operation = model.Navigate());
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Dispose();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && (AttachmentQueryText.IsFocused || AttachmentMetadataText.IsFocused
                || AttachmentMinimumBytes.IsFocused || AttachmentMaximumBytes.IsFocused || AttachmentDigest.IsFocused
                || AttachmentObservationSource.IsFocused || AttachmentDeviceProfile.IsFocused
                || AttachmentFileSourceId.IsFocused || AttachmentFileRevisionId.IsFocused || AttachmentFileItemId.IsFocused
                || AttachmentMinimumSessionGeneration.IsFocused || AttachmentMaximumSessionGeneration.IsFocused
                || AttachmentMinimumCapturedGeneration.IsFocused || AttachmentMaximumCapturedGeneration.IsFocused
                || AttachmentMinimumStorageRevision.IsFocused || AttachmentMaximumStorageRevision.IsFocused
                || AttachmentCapturedFrom.IsFocused || AttachmentCapturedUntil.IsFocused) && model.CanSearch)
            {
                Operation = model.Refresh();
                args.Handled = true;
            }
            if (args.Key == Key.Escape && !args.Handled) { Close(); args.Handled = true; }
        };

        void FindMetadata(SnapshotFindDirection direction)
        {
            model.FindMetadata(direction);
            if (!model.HasMetadataFindMatch) { return; }
            UpdateLayout();
            model.SynchronizeAuthority();
            if (!model.HasMetadataFindMatch) { return; }
            MetadataFindSpan.BringIntoView();
            model.SynchronizeAuthority();
            if (!model.HasMetadataFindMatch) { return; }
            if (!MetadataFindSpan.Focus())
            { model.MetadataFindFocusUnavailable(); }
            model.SynchronizeAuthority();
        }
    }

    private static void BlockTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
}
