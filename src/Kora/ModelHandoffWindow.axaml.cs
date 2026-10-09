using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kora;

internal sealed partial class ModelHandoffWindow : Window
{
    internal ModelHandoffWindow() => AvaloniaXamlLoader.Load(this);
}
