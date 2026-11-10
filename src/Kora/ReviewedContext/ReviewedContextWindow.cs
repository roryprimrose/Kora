using Avalonia.Controls;

namespace Kora;

internal sealed class ReviewedContextWindow : Window, IDisposable
{
    private readonly ReviewedContextView view;
#pragma warning disable VSTHRD003 // Compatibility adapter observes only operations started by its owned mixed-context view.
    internal Task Operation => view.Operation;
#pragma warning restore VSTHRD003
    internal T? FindControl<T>(string name) where T : Control => view.FindControl<T>(name);

    internal ReviewedContextWindow(SessionsViewModel model)
    {
        Title = "Review one mixed-context answer";
        Width = 780;
        Height = 780;
        DataContext = model;
        view = new(model, this);
        Content = view;
        view.CloseRequested += (_, _) => Close();
        Closed += (_, _) => view.Dispose();
    }
    public void Dispose() { Close(); view.Dispose(); }
}
