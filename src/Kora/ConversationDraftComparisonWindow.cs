using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Presentation;
using Kora.Controls;

namespace Kora;

internal sealed class ConversationDraftComparisonWindow : Window
{
    private bool opening = true;
    private Action? finishOpening;

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
        var before = new SnapshotComparisonFindPane(comparison.BaselineLabel, comparison.SavedBody, Current, Retire);
        var after = new SnapshotComparisonFindPane("Exact raw frozen unsaved composer memory buffer", comparison.Buffer, Current, Retire);
        Grid.SetColumn(after, 1);
        panes.Children.Add(before); panes.Children.Add(after);
        root.Children.Add(panes);
        Content = root;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void Erase()
        {
            before.Dispose(); after.Dispose();
            explanation.Text = string.Empty;
            comparison.Dispose();
        }
        EventHandler tick = (_, _) => { if (!Current()) { Retire(); } };
        EventHandler retired = (_, _) =>
        {
            Erase();
            Retire();
        };
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
            finishOpening = null;
            Erase();
            Content = null;
            model.CloseComposerComparison(comparison);
        };
        finishOpening = () =>
        {
            if (!Current()) { Hide(); Close(); }
            else { timer.Start(); }
        };
    }

    internal void Open(Window owner)
    {
        Show(owner);
        opening = false;
        finishOpening?.Invoke();
    }

    private void Retire()
    {
        // Erasure is synchronous; native disposal must not interrupt Avalonia's Show initialization.
        if (!opening) { Close(); }
    }
}
