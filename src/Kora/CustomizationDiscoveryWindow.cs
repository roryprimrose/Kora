using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Layout;

using Kora.Application.Tools;

namespace Kora;

internal sealed class CustomizationDiscoveryWindow : Window
{
    internal ListBox Sources { get; } = new() { SelectionMode = SelectionMode.Multiple, Height = 160 };
    internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBox Request { get; } = new() { AcceptsReturn = true, Height = 80, IsUndoEnabled = false };
    internal TextBox Preview { get; } = new() { AcceptsReturn = true, IsReadOnly = true, Height = 260, IsUndoEnabled = false };
    internal TextBox Result { get; } = new() { AcceptsReturn = true, IsReadOnly = true, Height = 150, IsUndoEnabled = false };
    internal TextBlock Status { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal Button Confirm { get; } = new() { Content = "Share only this reviewed metadata for ONE local answer", IsEnabled = false };

    internal CustomizationDiscoveryWindow(Action<string> run)
    {
        Title = "Discover selected customization metadata";
        Width = 880;
        Height = 950;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = CustomizationDiscoverySnapshot.Disclosure, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Add("Refresh supported source choices (no definition bodies)", "Sources");
        panel.Children.Add(new TextBlock { Text = "Select 1-6 exact sources. Paths here are PRIVATE chooser labels, never shared metadata." });
        panel.Children.Add(Sources);
        Add("Refresh existing Active conversations", "Targets");
        Add("Next conversation page", "NextTargets");
        panel.Children.Add(Targets);
        panel.Children.Add(new TextBlock { Text = "Original question (retained in the explicitly selected conversation)" });
        panel.Children.Add(Request);
        Add("Read selected primary metadata and preview exact shared fields", "Review");
        panel.Children.Add(Preview);
        panel.Children.Add(Confirm);
        Confirm.Click += (_, _) => run("Confirm");
        Add("Cancel / close and join owned work", "Close");
        panel.Children.Add(Status);
        panel.Children.Add(Result);
        Content = new ScrollViewer { Content = panel };
        AutomationProperties.SetName(Sources, "Private exact source choices; paths are not shared");
        AutomationProperties.SetName(Targets, "Exact Active conversation");
        AutomationProperties.SetName(Request, "Original customization discovery question");
        AutomationProperties.SetName(Preview, "Complete frozen metadata fields shared for one local request");
        AutomationProperties.SetName(Result, "Genuine retained answer or terminal outcome");
        void Add(string label, string action)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => run(action);
            panel.Children.Add(button);
        }
    }

    internal void Clear()
    {
        Sources.ItemsSource = null;
        Targets.ItemsSource = null;
        Request.Text = Preview.Text = Result.Text = Status.Text = string.Empty;
        Confirm.IsEnabled = false;
    }
}
