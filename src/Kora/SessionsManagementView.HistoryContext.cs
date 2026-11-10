namespace Kora;

internal sealed partial class SessionsManagementView
{
    private ConversationHistoryWindow? historyContextWindow;
    private ConversationHistoryView? historyContextView;
    internal Task HistoryContextOperation { get; private set; } = Task.CompletedTask;
    private void BindHistoryContext(SessionsViewModel model)
    {
        BindModelHistoryContext(model);
        ReviewHistoryContext.Click += async (_, _) =>
        {
            await (HistoryContextOperation = OpenHistoryContextAsync(model));
        };
        Closed += (_, _) => { historyContextView?.Dispose(); historyContextView = null; historyContextWindow?.Close(); historyContextWindow = null; };
    }

    private Task OpenHistoryContextAsync(SessionsViewModel model)
    {
        try
        {
            if (!IsActive || !model.CanOpenHistoryContext)
            { throw new InvalidOperationException("The original history-selection presenter is unavailable."); }
            modelHistoryContextWindow?.Close();
            modelHistoryContextView?.Dispose();
            modelHistoryContextView = null;
            if (historyContextView is { } existing && model.HasHistoryContext) { existing.Activate(); return Task.CompletedTask; }
            historyContextView?.Dispose();
            historyContextView = null;
            if (historyContextWindow is { } adapter) { adapter.Activate(); return Task.CompletedTask; }
            model.OpenHistoryContext();
            if (Owner is ResponseWindow)
            {
                var view = new ConversationHistoryView(model, Owner);
                historyContextView = view;
                view.CloseRequested += (_, _) => { view.Dispose(); if (ReferenceEquals(historyContextView, view)) { historyContextView = null; } };
                view.Show();
            }
            else
            {
                var window = new ConversationHistoryWindow(model);
                historyContextWindow = window;
                window.Closed += (_, _) => { if (ReferenceEquals(historyContextWindow, window)) { historyContextWindow = null; } };
                window.Show(Owner);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportModelHistoryContextFailure(exception); }
        return Task.CompletedTask;
    }
}
