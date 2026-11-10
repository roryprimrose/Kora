namespace Kora;

internal sealed partial class SessionsManagementView
{
    private ReviewedContextWindow? mixedContextWindow;
    private ReviewedContextView? mixedContextView;
    internal Task MixedContextOperation { get; private set; } = Task.CompletedTask;

    private void BindMixedContext(SessionsViewModel model)
    {
        OpenMixedContext.Click += (_, _) =>
        {
            MixedContextOperation = OpenMixedContextAsync(model);
        };
        Closed += (_, _) => { mixedContextView?.Dispose(); mixedContextView = null; mixedContextWindow?.Close(); };
    }

    private Task OpenMixedContextAsync(SessionsViewModel model)
    {
        try
        {
            if (!IsActive || !model.CanOpenMixedContext)
            { throw new InvalidOperationException("The original mixed-context presenter is unavailable."); }
            if (mixedContextView is not null && model.HasMixedContext) { mixedContextView.Activate(); return Task.CompletedTask; }
            mixedContextView?.Dispose();
            mixedContextView = null;
            if (mixedContextWindow is not null) { mixedContextWindow.Activate(); return Task.CompletedTask; }
            model.OpenMixedContext();
            if (Owner is ResponseWindow)
            {
                var view = new ReviewedContextView(model, Owner);
                mixedContextView = view;
                view.CloseRequested += (_, _) => { view.Dispose(); if (ReferenceEquals(mixedContextView, view)) { mixedContextView = null; } };
                view.Show();
            }
            else
            {
                var window = new ReviewedContextWindow(model);
                mixedContextWindow = window;
                System.ComponentModel.PropertyChangedEventHandler changed = (_, _) =>
                {
                    if (!model.MixedContextOpen) { window.Close(); }
                };
                model.PropertyChanged += changed;
                window.Closed += (_, _) => model.PropertyChanged -= changed;
                window.Closed += (_, _) => mixedContextWindow = null;
                window.Show(Owner);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportMixedContextFailure(exception); }
        return Task.CompletedTask;
    }
}
