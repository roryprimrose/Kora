using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kora;

internal sealed partial class ModelHandoffView : UserControl
{
    internal ModelHandoffView() => AvaloniaXamlLoader.Load(this);
}
