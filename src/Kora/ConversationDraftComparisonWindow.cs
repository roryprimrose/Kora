using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Presentation;
using Kora.Controls;

namespace Kora;

internal sealed class ConversationDraftComparisonWindow : Window
{
    internal ConversationDraftComparisonWindow(Window owner, SessionsViewModel model, ConversationDraftComparison comparison)
    {
        Title = "Passive exact-session composer comparison";
        Width = 1000; Height = 720; MinWidth = 640; MinHeight = 500;
        var root = new DockPanel { Margin = new(12) };
        var explanation = new NamedTextBlock { Text = comparison.Summary, TextWrapping = TextWrapping.Wrap };
        var disclosure = new ScrollViewer { Content = explanation, MaxHeight = 160 };
        DockPanel.SetDock(disclosure, Dock.Top);
        root.Children.Add(disclosure);
        var close = new Button { Content = "Close", Margin = new(0, 10) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        var panes = new Grid { ColumnDefinitions = new("*,*") };
        bool Current() => owner.IsVisible && IsVisible && comparison.Available;
        var before = new SnapshotComparisonFindPane(comparison.BaselineLabel, comparison.SavedBody, Current, Close);
        var after = new SnapshotComparisonFindPane("Exact raw frozen unsaved composer memory buffer", comparison.Buffer, Current, Close);
        Grid.SetColumn(after, 1);
        panes.Children.Add(before); panes.Children.Add(after);
        root.Children.Add(panes);
        Content = root;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        EventHandler tick = (_, _) => { if (!Current()) { Close(); } };
        EventHandler retired = (_, _) => Close();
        timer.Tick += tick;
        model.ComposerComparisonRetired += retired;
        owner.Closed += retired;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Close(); }
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            model.ComposerComparisonRetired -= retired;
            owner.Closed -= retired;
            before.Dispose(); after.Dispose();
            explanation.Text = string.Empty;
            Content = null;
            comparison.Dispose();
            model.CloseComposerComparison(comparison);
        };
        Opened += (_, _) =>
        {
            // Reentrant opening callbacks may revoke ownership before Show finishes.
            if (!Current()) { Hide(); Close(); }
            else { timer.Start(); }
        };
    }
}
