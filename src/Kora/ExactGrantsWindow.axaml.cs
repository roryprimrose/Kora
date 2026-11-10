using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.Interaction;

namespace Kora;

public sealed partial class ExactGrantsWindow : Window
{
    internal ExactGrantsView View { get; }
    internal T? FindControl<T>(string name) where T : Control => View.FindControl<T>(name);

    public ExactGrantsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        View = new(this, () => IsVisible && WindowState != WindowState.Minimized);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }

    public ExactGrantsWindow(ExactGrantsViewModel state) : this() => DataContext = state;
}
