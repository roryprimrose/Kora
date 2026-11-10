using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Kora;

internal sealed partial class ConversationHistoryView : UserControl, IDisposable
{
    internal Task Operation { get; private set; } = Task.CompletedTask;
    private readonly SessionsViewModel model;
    private readonly SessionContextReviewPresentation presentation;
    internal event EventHandler? CloseRequested;
    internal void Show() => presentation.Show();
    internal void Activate() => presentation.Show();
    internal void Close() => Dispose();
    public ConversationHistoryView() => throw new InvalidOperationException("History context requires its exact session and native owner.");

    internal ConversationHistoryView(SessionsViewModel model, Window owner)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        presentation = new(this, owner, model, () => model.HasHistoryContext,
            model.ClearHistoryContext, "Review earlier conversation messages");
        SizeChanged += (_, _) => ReviewLayout.Height = Math.Max(500, Bounds.Height - 24);
        Sources.SelectionChanged += (_, _) => model.SelectHistoryContext(
            Sources.SelectedItems?.OfType<SessionsViewModel.HistoryContextCandidate>() ?? []);
        Review.Click += async (_, _) => await (Operation = model.ReviewHistoryContextAsync());
        Confirm.Click += async (_, _) => await (Operation = model.ConfirmHistoryContextAsync());
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
}
