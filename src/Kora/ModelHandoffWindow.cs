using Avalonia.Controls;

namespace Kora;

internal sealed class ModelHandoffWindow : Window
{
    internal ModelHandoffView View { get; } = new();
    internal ModelHandoffWindow()
    {
        Title = "Exact local provider handoff review";
        Width = 960; Height = 760; MinWidth = 720; MinHeight = 520;
        Content = View;
        NameScope.SetNameScope(this, NameScope.GetNameScope(View));
    }
}
