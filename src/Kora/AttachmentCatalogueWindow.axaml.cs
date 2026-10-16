using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AttachmentCatalogueWindow : Window
{
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal AttachmentCatalogueWindow(AttachmentCatalogueViewModel model)
    {
        InitializeComponent();
        DataContext = model;
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
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }
}
