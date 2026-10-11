using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kora;

internal sealed class OwnedInstructionEditorWindow : Window
{
    internal readonly ComboBox Files = new() { Width = 240 };
    internal readonly TextBox NameInput = new() { Width = 240, PlaceholderText = "lowercase-owned-id" };
    internal readonly TextBox Source = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private Window? activeReview;

    internal OwnedInstructionEditorWindow(Action<string> run)
    {
        Title = "Kora-owned instruction editor";
        Width = 960;
        Height = 720;
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(Files);
        controls.Children.Add(NameInput);
        foreach (var action in new[] { "List", "Create", "Inspect", "Save", "Reload", "Delete", "Review use" })
        {
            var button = new Button { Content = action, Margin = new(3) };
            button.Click += (_, _) => run(action);
            controls.Children.Add(button);
        }
        var panel = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);
        var disclosure = new TextBlock
        {
            Text = "Only Kora roaming Instructions/*.instructions.md. Profile files and bundled definitions are read-only. "
                + "Native source editing only; no model, dictation, scripts, rename, restore or fork. "
                + "Save is not activation. Use review permits explicit instruction invocation for a new request only.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 10),
        };
        DockPanel.SetDock(disclosure, Dock.Top);
        panel.Children.Add(disclosure);
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);
        panel.Children.Add(Source);
        Content = panel;
    }

    internal void Report(string text) => status.Text = text;
    internal void CancelReview() => activeReview?.Close();
    internal void ClearPrivateText() { Source.Text = string.Empty; NameInput.Text = string.Empty; Files.ItemsSource = null; status.Text = string.Empty; }

    internal async Task<int> Choose(string title, string details, string? original, string? proposed,
        string[] choices, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Choose(title, details, original, proposed, choices, token)); }
        var dialog = new Window { Title = title, Width = 900, Height = 680, CanResize = true };
        var root = new DockPanel { Margin = new(12) };
        var explanation = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(explanation, Dock.Top);
        root.Children.Add(explanation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new(0, 10) };
        for (var index = 0; index < choices.Length; index++)
        {
            var answer = index;
            var button = new Button { Content = choices[index] };
            button.Click += (_, _) => dialog.Close(answer);
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var sources = new Grid { ColumnDefinitions = new("*,*") };
        var before = new TextBox { Text = original ?? "(missing/new file)", IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas") };
        var after = new TextBox { Text = proposed ?? "(delete)", IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas") };
        Grid.SetColumn(after, 1);
        sources.Children.Add(before);
        sources.Children.Add(after);
        root.Children.Add(sources);
        dialog.Content = root;
        activeReview = dialog;
        var reply = dialog.ShowDialog<int?>(this);
        using var registration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close()));
        try { return await reply ?? choices.Length - 1; }
        finally
        {
            if (ReferenceEquals(activeReview, dialog)) { activeReview = null; }
            before.Text = string.Empty; after.Text = string.Empty; explanation.Text = string.Empty;
        }
    }
}
