using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class SessionsManagementView
{
    private ModelHistoryContextWindow? modelHistoryContextWindow;
    private ModelHistoryContextView? modelHistoryContextView;
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
                if (modelHistoryContextView is { } presented && model.HasModelHistoryContext) { presented.Activate(); return; }
                modelHistoryContextView?.Dispose();
                modelHistoryContextView = null;
                if (modelHistoryContextWindow is { } existing) { existing.Activate(); return; }
                historyContextView?.Dispose();
                historyContextView = null;
                if (historyContextWindow is not null)
                {
                    historyContextWindow.Close();
                    historyContextWindow = null;
                }
                var admitted = presenter.Capture();
                await (ModelHistoryContextOperation = model.OpenModelHistoryContextAsync(admitted));
                if (!admitted() || !model.HasModelHistoryContext) { model.ClearHistoryContext(); return; }
                if (Owner is ResponseWindow)
                {
                    var view = new ModelHistoryContextView(model, Owner);
                    modelHistoryContextView = view;
                    view.CloseRequested += (_, _) => { view.Dispose(); if (ReferenceEquals(modelHistoryContextView, view)) { modelHistoryContextView = null; } };
                    view.Show();
                    return;
                }
                var window = new ModelHistoryContextWindow(model);
                modelHistoryContextWindow = window;
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(modelHistoryContextWindow, window)) { modelHistoryContextWindow = null; }
                };
                window.Show(Owner);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { model.ReportModelHistoryContextFailure(exception); }
        }
        Closed += (_, _) => { presenter.Dispose(); modelHistoryContextView?.Dispose(); modelHistoryContextView = null; modelHistoryContextWindow?.Close(); modelHistoryContextWindow = null; };
    }
}
