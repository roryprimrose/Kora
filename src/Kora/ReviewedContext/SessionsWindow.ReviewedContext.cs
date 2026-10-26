namespace Kora;

internal sealed partial class SessionsWindow
{
    private ReviewedContextWindow? mixedContextWindow;

    private void BindMixedContext(SessionsViewModel model)
    {
        OpenMixedContext.Click += (_, _) =>
        {
            if (mixedContextWindow is not null) { mixedContextWindow.Activate(); return; }
            model.OpenMixedContext();
            var window = new ReviewedContextWindow(model);
            mixedContextWindow = window;
            System.ComponentModel.PropertyChangedEventHandler changed = (_, _) =>
            {
                if (!model.MixedContextOpen) { window.Close(); }
            };
            model.PropertyChanged += changed;
            window.Closed += (_, _) => model.PropertyChanged -= changed;
            window.Closed += (_, _) => mixedContextWindow = null;
            window.Show(this);
        };
        Closed += (_, _) => mixedContextWindow?.Close();
    }
}
