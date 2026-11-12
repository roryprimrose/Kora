using Avalonia;
using Avalonia.Controls;

namespace Kora.Controls;

public sealed class SettingsPage : ContentControl
{
    protected override Type StyleKeyOverride => typeof(ContentControl);

    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<SettingsPage, string>(nameof(Header), string.Empty);

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
