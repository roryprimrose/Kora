using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Styling;

namespace Kora.Controls;

public sealed class NavigableTabControl : TabControl
{
    public NavigableTabControl()
    {
        Styles.Add(new Style(selector => selector.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(IsTabStopProperty, new Binding(nameof(TabItem.IsSelected))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.Self),
                }),
            },
        });
    }

    protected override Type StyleKeyOverride => typeof(TabControl);

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        // ItemsControl remembers content focus as its Tab entry point, which can
        // return to the same reader/input when the owning window wraps navigation.
        KeyboardNavigation.SetTabOnceActiveElement(this, null);
    }
}
