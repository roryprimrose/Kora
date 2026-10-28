using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private ModelHistoryContextWindow? modelHistoryContextWindow;
    internal Task ModelHistoryContextOperation { get; private set; } = Task.CompletedTask;

    private void BindModelHistoryContext(SessionsViewModel model)
    {
        var presenter = new ReplyPresentationFence(this);
        AnswerSourceRecords.SelectionChanged += (_, _) => model.RefreshModelHistoryContextAvailability();
        ReviewModelHistoryContext.Click += OpenAdvisory;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
            Justification = "Native Click boundary awaits the tracked binding and reports failures through the view model.")]
        async void OpenAdvisory(object? sender, RoutedEventArgs args)
        {
            try
            {
                if (!model.CanOpenModelHistoryContext) { return; }
                if (modelHistoryContextWindow is { } existing) { existing.Activate(); return; }
                if (historyContextWindow is not null)
                {
                    historyContextWindow.Close();
                    historyContextWindow = null;
                }
                await (ModelHistoryContextOperation = model.OpenModelHistoryContextAsync(presenter.Capture()));
                if (!IsVisible || !model.HasModelHistoryContext) { return; }
                var window = new ModelHistoryContextWindow(model);
                modelHistoryContextWindow = window;
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(modelHistoryContextWindow, window)) { modelHistoryContextWindow = null; }
                };
                window.Show(this);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { model.ReportModelHistoryContextFailure(exception); }
        }
        Closed += (_, _) => { presenter.Dispose(); modelHistoryContextWindow?.Close(); modelHistoryContextWindow = null; };
    }
}
