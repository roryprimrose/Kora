using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SavedDraftCatalogueWindow : Window
{
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal SavedDraftCatalogueWindow(SavedDraftCatalogueViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        RefreshPage.Click += async (_, _) => await (Operation = model.Refresh());
        NextPage.Click += async (_, _) => await (Operation = model.Refresh(next: true));
        CancelRead.Click += (_, _) => model.Cancel();
        DraftRecords.SelectionChanged += (_, _) => model.Select(DraftRecords.SelectedItem as SavedDraftSummary);
        OpenExact.Click += async (_, _) => await (Operation = model.Navigate());
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Dispose();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }
}
