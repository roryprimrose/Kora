using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SavedDraftCatalogueWindow : Window
{
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal bool PrepareExactId(Guid sessionId, Func<bool> admitted, Func<bool> sourceAdmitted)
    {
        if (!IsVisible || !admitted()) { return false; }
        UpdateLayout();
        DraftQueryText.BringIntoView();
        if (!DraftQueryText.Focus() || !admitted() || !sourceAdmitted()
            || DataContext is not SavedDraftCatalogueViewModel model) { return false; }
        return model.PrepareExactId(sessionId) && IsVisible && admitted()
            && model.Mode == SavedDraftCatalogueMode.ExactId
            && string.Equals(model.QueryText, sessionId.ToString("D"), StringComparison.Ordinal);
    }
    internal SavedDraftCatalogueWindow(SavedDraftCatalogueViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        RefreshPage.Click += async (_, _) => await (Operation = model.Refresh());
        NextPage.Click += async (_, _) => await (Operation = model.Refresh(next: true));
        CancelRead.Click += (_, _) => model.Cancel();
        ClearQuery.Click += (_, _) => model.ClearQuery();
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
