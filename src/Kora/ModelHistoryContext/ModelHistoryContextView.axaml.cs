using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;

namespace Kora;

internal sealed partial class ModelHistoryContextView : UserControl, IDisposable
{
    private readonly SessionsViewModel model;
    private readonly SessionContextReviewPresentation presentation;
    internal event EventHandler? CloseRequested;
    internal void Show() => presentation.Show();
    internal void Activate() => presentation.Show();
    internal void Close() => Dispose();
    internal Task Operation { get; private set; } = Task.CompletedTask;
    public ModelHistoryContextView() => throw new InvalidOperationException("The model advisory requires its original question and native owner.");

    internal ModelHistoryContextView(SessionsViewModel model, Window owner)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        presentation = new(this, owner, model, () => model.HasModelHistoryContext,
            model.ClearHistoryContext, "Review context for the original question");
        SizeChanged += (_, _) => ReviewLayout.Height = Math.Max(500, Bounds.Height - 24);
        Sources.SelectionChanged += (_, _) => model.SelectHistoryContext(
            Sources.SelectedItems?.OfType<SessionsViewModel.HistoryContextCandidate>() ?? []);
        CompleteReview.CopyingToClipboard += BlockReviewTransfer;
        CompleteReview.CuttingToClipboard += BlockReviewTransfer;
        CompleteReview.PastingFromClipboard += BlockReviewTransfer;
        CompleteReview.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
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
        CloseView.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; CloseRequested?.Invoke(this, EventArgs.Empty); } };
        var expiry = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => model.RefreshHistoryContextReview());
        expiry.Start();
        presentation.Retired += (_, _) => expiry.Stop();
        presentation.Closed += (_, _) =>
        {
            expiry.Stop();
            model.ClearHistoryContext();
            DataContext = null;
            Sources.ItemsSource = null;
        };
    }

    public void Dispose()
    {
        presentation.Dispose();
        model.ClearHistoryContext();
        DataContext = null;
        Sources.ItemsSource = null;
    }

    private static void BlockReviewTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;
}
