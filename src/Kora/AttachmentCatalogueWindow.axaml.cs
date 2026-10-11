using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AttachmentCatalogueWindow : Window
{
    internal AttachmentCatalogueWindow(AttachmentCatalogueViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        RefreshPage.Click += async (_, _) => await model.Refresh();
        NextPage.Click += async (_, _) => await model.Refresh(next: true);
        CancelRead.Click += (_, _) => model.Cancel();
        SlotRecords.SelectionChanged += (_, _) => model.Select(SlotRecords.SelectedItem as SessionAttachmentSummary);
        OpenExact.Click += async (_, _) => await model.Navigate();
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Dispose();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }
}
