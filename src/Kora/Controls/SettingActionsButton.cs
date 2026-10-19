using System.Windows.Input;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kora.Controls;

public sealed class SettingActionsButton : Button
{
    public static readonly StyledProperty<string> SettingNameProperty =
        AvaloniaProperty.Register<SettingActionsButton, string>(nameof(SettingName), string.Empty);

    public static readonly StyledProperty<ICommand?> ResetCommandProperty =
        AvaloniaProperty.Register<SettingActionsButton, ICommand?>(nameof(ResetCommand));

    private static readonly Geometry CogGeometry = Geometry.Parse(
        "F0 M7.5,0 H10.5 L11,2 12,2.6 14,1.6 16.4,4 15.4,6 16,7 18,7.5 "
        + "V10.5 L16,11 15.4,12 16.4,14 14,16.4 12,15.4 11,16 10.5,18 "
        + "H7.5 L7,16 6,15.4 4,16.4 1.6,14 2.6,12 2,11 0,10.5 "
        + "V7.5 L2,7 2.6,6 1.6,4 4,1.6 6,2.6 7,2 Z "
        + "M9,5.5 A3.5,3.5 0 1 0 9,12.5 A3.5,3.5 0 1 0 9,5.5 Z");

    private readonly MenuItem resetItem;

    public SettingActionsButton()
    {
        Content = new PathIcon { Data = CogGeometry, Width = 16, Height = 16 };
        Width = 32;
        Height = 32;
        Padding = new Thickness(6);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        resetItem = new MenuItem { Header = "Reset setting" };
        Flyout = new MenuFlyout { Items = { resetItem } };
    }

    public string SettingName
    {
        get => GetValue(SettingNameProperty);
        set => SetValue(SettingNameProperty, value);
    }

    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SettingNameProperty)
        {
            var name = $"{SettingName} actions";
            AutomationProperties.SetName(this, name);
            ToolTip.SetTip(this, name);
            AutomationProperties.SetName(resetItem, $"Reset {SettingName} to its default");
        }
        else if (change.Property == ResetCommandProperty)
        {
            resetItem.Command = ResetCommand;
        }
    }
}
