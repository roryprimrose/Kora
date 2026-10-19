using System.Windows.Input;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Kora.Controls;

public sealed class SettingActionsButton : Button
{
    public static readonly StyledProperty<string> SettingNameProperty =
        AvaloniaProperty.Register<SettingActionsButton, string>(nameof(SettingName), string.Empty);

    public static readonly StyledProperty<ICommand?> ResetCommandProperty =
        AvaloniaProperty.Register<SettingActionsButton, ICommand?>(nameof(ResetCommand));

    private readonly MenuItem resetItem;

    public SettingActionsButton()
    {
        Content = "...";
        Padding = new Thickness(8, 4);
        VerticalAlignment = VerticalAlignment.Center;
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
