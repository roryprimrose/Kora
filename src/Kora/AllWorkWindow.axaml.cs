using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AllWorkWindow : Window
{
    internal AllWorkWindow(AllWorkViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Refresh.Click += async (_, _) => await model.RefreshAsync();
        Search.Click += async (_, _) => await model.RefreshAsync();
        ClearQuery.Click += (_, _) => { model.ClearQuery(); MetadataQuery.Focus(); };
        ClearRanges.Click += (_, _) => model.ClearRanges();
        ClearTaskQuery.Click += (_, _) => { model.ClearTaskQuery(); RetainedTaskId.Focus(); };
        RetainedTaskId.KeyDown += (_, args) => ClearTaskCorrelation(args);
        RetainedRequestId.KeyDown += (_, args) => ClearTaskCorrelation(args);
        void ClearTaskCorrelation(KeyEventArgs args)
        {
            if (args.Key != Key.Escape) { return; }
            model.ClearTaskQuery();
            args.Handled = true;
        }
        MetadataQuery.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                model.ClearQuery();
                args.Handled = true;
            }
            else if (args.Key == Key.Enter && model.CanSearch)
            {
                args.Handled = true;
                await model.RefreshAsync();
            }
        };
        Next.Click += async (_, _) => await model.RefreshAsync(next: true);
        CancelRead.Click += (_, _) => model.Cancel();
        Navigate.Click += async (_, _) => await model.NavigateAsync();
        Records.SelectionChanged += (_, _) => model.Select(Records.SelectedItem as SessionWorkOverviewRow);
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Close();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }
}
