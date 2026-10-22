namespace Kora;

internal sealed partial class SessionsWindow
{
    private ConversationHistoryWindow? historyContextWindow;
    private void BindHistoryContext(SessionsViewModel model)
    {
        ReviewHistoryContext.Click += (_, _) =>
        {
            if (!model.CanOpenHistoryContext) { return; }
            if (historyContextWindow is { } existing) { existing.Activate(); return; }
            model.OpenHistoryContext();
            var window = new ConversationHistoryWindow(model);
            historyContextWindow = window;
            window.Closed += (_, _) => { if (ReferenceEquals(historyContextWindow, window)) { historyContextWindow = null; } };
            window.Show(this);
        };
        Closed += (_, _) => { historyContextWindow?.Close(); historyContextWindow = null; };
    }
}
