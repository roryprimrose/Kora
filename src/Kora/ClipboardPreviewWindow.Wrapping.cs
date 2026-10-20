using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private CheckBox? wrapSnapshot;
    private ScrollViewer? wrappingViewport;
    private EventHandler<RoutedEventArgs>? wrappingChanged;
    private TextWrapping sourceWrappingDefault;
    private long wrappingOperation;

    private void AddSourceWrapping(StackPanel chrome, ClipboardSnapshot snapshot, ScrollViewer viewport)
    {
        sourceWrappingDefault = source.TextWrapping;
        var toggle = new CheckBox
        {
            Content = "Wrap snapshot text",
            IsChecked = sourceWrappingDefault != TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(toggle, "Wrap snapshot text");
        wrapSnapshot = toggle;
        wrappingViewport = viewport;
        var generation = findGeneration;
        wrappingChanged = (_, _) =>
        {
            if (!ReferenceEquals(wrapSnapshot, toggle) || generation != findGeneration) { return; }
            var operation = ++wrappingOperation;
            bool Current() => ReferenceEquals(wrapSnapshot, toggle) && ReferenceEquals(wrappingViewport, viewport)
                && ReferenceEquals(findSource, snapshot) && operation == wrappingOperation
                && EnsureFindCurrent(generation)
                && ReferenceEquals(wrapSnapshot, toggle) && ReferenceEquals(findSource, snapshot)
                && operation == wrappingOperation;
            try
            {
                if (!Current()) { return; }
                var wrap = toggle.IsChecked == true;
                source.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
                if (!Current()) { return; }
                // Disabled, not Hidden: a wrapped source must be measured at the viewport width.
                viewport.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
                if (!Current()) { return; }
                UpdateLayout();
                _ = Current();
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(wrapSnapshot, toggle) && generation == findGeneration) { ClearAndClose(); }
                reportFailure(exception);
            }
        };
        toggle.IsCheckedChanged += wrappingChanged;
        chrome.Children.Add(toggle);
    }

    private void ClearWrappingState()
    {
        wrappingOperation++;
        var toggle = wrapSnapshot;
        var viewport = wrappingViewport;
        wrapSnapshot = null;
        wrappingViewport = null;
        if (toggle is null) { return; }
        toggle.IsCheckedChanged -= wrappingChanged;
        wrappingChanged = null;
        toggle.IsChecked = sourceWrappingDefault != TextWrapping.NoWrap;
        source.TextWrapping = sourceWrappingDefault;
        if (viewport is not null) { viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; }
    }
}
