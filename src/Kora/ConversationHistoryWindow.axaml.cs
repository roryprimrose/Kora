using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Kora;

internal sealed partial class ConversationHistoryWindow : Window
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    internal ConversationHistoryWindow(SessionsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Sources.SelectionChanged += (_, _) => model.SelectHistoryContext(
            Sources.SelectedItems?.OfType<SessionsViewModel.HistoryContextCandidate>() ?? []);
        Review.Click += async (_, _) => await (Operation = model.ReviewHistoryContextAsync());
        Confirm.Click += async (_, _) => await (Operation = model.ConfirmHistoryContextAsync());
        CloseView.Click += (_, _) => Close();
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
        var expiry = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => model.RefreshHistoryContextReview());
        expiry.Start();
        Closed += (_, _) =>
        {
            expiry.Stop();
            model.ClearHistoryContext();
            DataContext = null;
            Sources.ItemsSource = null;
        };
    }
}
