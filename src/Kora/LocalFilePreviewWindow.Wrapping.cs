using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private StackPanel? wrappingPanel;
    private ScrollViewer? wrappingViewport;
    private CheckBox? wrappingToggle;
    private EventHandler<RoutedEventArgs>? wrappingChanged;
    private TextWrapping sourceDefaultWrapping;
    private long sourceLayoutGeneration;

    private void PrepareSourceWrapping(DockPanel body, ScrollViewer viewport)
    {
        wrappingViewport = viewport;
        sourceDefaultWrapping = content.TextWrapping;
        wrappingPanel = new StackPanel();
        DockPanel.SetDock(wrappingPanel, Dock.Top);
        body.Children.Add(wrappingPanel);
    }

    private void SetWrappingSource(LocalFileRevision? file)
    {
        if (ReferenceEquals(findSource, file)) { return; }
        ClearSourceWrapping();
        if (file is null || wrappingPanel is null || wrappingViewport is null) { return; }
        var reader = content;
        var viewport = wrappingViewport;
        reader.TextWrapping = sourceDefaultWrapping;
        viewport.HorizontalScrollBarVisibility = sourceDefaultWrapping == TextWrapping.NoWrap
            ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        var toggle = new CheckBox
        {
            Content = "Wrap admitted text",
            IsChecked = sourceDefaultWrapping != TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(toggle, "Wrap admitted text");
        AutomationProperties.SetHelpText(toggle,
            "Presentation only for this admitted source. Visual rows are not logical lines or citation positions. No read or saved preference.");
        wrappingToggle = toggle;
        wrappingChanged = (_, _) =>
        {
            if (!ReferenceEquals(wrappingToggle, toggle) || !ReferenceEquals(content, reader)
                || !ReferenceEquals(findSource, file)) { return; }
            var current = isSearchCurrent?.Invoke() == true;
            // The current-source predicate can reenter native presentation.
            if (!ReferenceEquals(wrappingToggle, toggle) || !ReferenceEquals(content, reader)
                || !ReferenceEquals(findSource, file)) { return; }
            if (!current)
            {
                ClearContent();
                reportFailure("Text wrapping unavailable; the exact source admission is no longer current. Inspect again.");
                return;
            }
            var generation = ++sourceLayoutGeneration;
            reader.TextWrapping = toggle.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (generation != sourceLayoutGeneration || !ReferenceEquals(wrappingToggle, toggle)) { return; }
            viewport.HorizontalScrollBarVisibility = toggle.IsChecked == true
                ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        };
        toggle.IsCheckedChanged += wrappingChanged;
        wrappingPanel.Children.Add(toggle);
    }

    private void ClearSourceWrapping()
    {
        sourceLayoutGeneration++;
        if (wrappingToggle is not null)
        {
            wrappingToggle.IsCheckedChanged -= wrappingChanged;
            wrappingToggle.IsEnabled = false;
        }
        wrappingChanged = null;
        wrappingToggle = null;
        wrappingPanel?.Children.Clear();
    }

    private bool IsSourceLayoutCurrent(long generation, NamedTextBlock span, LocalFileRevision file, Func<bool> current) =>
        current() && generation == sourceLayoutGeneration && ReferenceEquals(citedSpan, span)
        && ReferenceEquals(findSource, file);
}
