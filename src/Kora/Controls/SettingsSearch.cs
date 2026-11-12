using Avalonia;
using Avalonia.Controls;

namespace Kora.Controls;

public sealed class SettingsSearch : AvaloniaObject
{
    public static readonly AttachedProperty<string> IdProperty =
        AvaloniaProperty.RegisterAttached<SettingsSearch, Control, string>("Id", string.Empty);
    public static readonly AttachedProperty<string> GroupProperty =
        AvaloniaProperty.RegisterAttached<SettingsSearch, Control, string>("Group", string.Empty);
    public static readonly AttachedProperty<string> ValueProperty =
        AvaloniaProperty.RegisterAttached<SettingsSearch, Control, string>("Value", string.Empty);
    public static readonly AttachedProperty<string> AliasesProperty =
        AvaloniaProperty.RegisterAttached<SettingsSearch, Control, string>("Aliases", string.Empty);

    public static string GetId(Control control) => control.GetValue(IdProperty);
    public static void SetId(Control control, string value) => control.SetValue(IdProperty, value);
    public static string GetGroup(Control control) => control.GetValue(GroupProperty);
    public static void SetGroup(Control control, string value) => control.SetValue(GroupProperty, value);
    public static string GetValue(Control control) => control.GetValue(ValueProperty);
    public static void SetValue(Control control, string value) => control.SetValue(ValueProperty, value);
    public static string GetAliases(Control control) => control.GetValue(AliasesProperty);
    public static void SetAliases(Control control, string value) => control.SetValue(AliasesProperty, value);
}
