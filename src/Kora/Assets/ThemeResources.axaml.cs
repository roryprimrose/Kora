using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kora.Theming;

public sealed partial class ThemeResources : ResourceDictionary
{
    public ThemeResources() => AvaloniaXamlLoader.Load(this);
}
