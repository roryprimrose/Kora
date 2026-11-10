using Avalonia.Controls;

namespace Kora;

internal sealed partial class ConversationHistoryWindow : Window, IDisposable
{
    private readonly ConversationHistoryView view;
#pragma warning disable VSTHRD003 // Compatibility adapter observes only operations started by its owned context view.
    internal Task Operation => view.Operation;
#pragma warning restore VSTHRD003
    internal T? FindControl<T>(string name) where T : Control => view.FindControl<T>(name);

    internal ConversationHistoryWindow(SessionsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        view = new(model, this);
        Content = view;
        view.CloseRequested += (_, _) => Close();
        Closed += (_, _) => view.Dispose();
    }
    public void Dispose() { Close(); view.Dispose(); }
}
