using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;

namespace Kora;

internal sealed partial class ModelHistoryContextWindow : Window
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    internal ModelHistoryContextWindow(SessionsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible || args.Property == StyledElement.DataContextProperty)
            { model.ClearHistoryContext(); }
        };
        Sources.SelectionChanged += (_, _) => model.SelectHistoryContext(
            Sources.SelectedItems?.OfType<SessionsViewModel.HistoryContextCandidate>() ?? []);
        Review.Click += ReviewClicked;
        Confirm.Click += ConfirmClicked;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
            Justification = "Native Click boundary awaits the tracked operation and reports failures through the view model.")]
        async void ReviewClicked(object? sender, RoutedEventArgs args)
        {
            try { await (Operation = model.ReviewHistoryContextAsync()); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { model.ReportModelHistoryContextFailure(exception); }
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
            Justification = "Native Click boundary awaits the tracked operation and reports failures through the view model.")]
        async void ConfirmClicked(object? sender, RoutedEventArgs args)
        {
            try { await (Operation = model.ConfirmHistoryContextAsync()); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { model.ReportModelHistoryContextFailure(exception); }
        }
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
