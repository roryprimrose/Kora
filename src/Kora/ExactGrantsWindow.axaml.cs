using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.Interaction;

namespace Kora;

public sealed partial class ExactGrantsWindow : Window
{
    public ExactGrantsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public ExactGrantsWindow(ExactGrantsViewModel state) : this()
    {
        DataContext = state;
    }
}
