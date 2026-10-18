using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Definitions;

namespace Kora;

internal sealed class OwnedInstructionRebaseReviewWindow : Window
{
    private readonly List<TextBox> bodies = [];
    private readonly TextBlock explanation;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Func<bool>? current;
    private bool withdrawalQueued;
    private readonly TaskCompletionSource withdrawal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal OwnedInstructionRebaseReviewWindow(OwnedDraftRebase proposal, Func<bool> eligible)
    {
        current = eligible;
        Title = "Review source-preserving Rebase";
        Width = 1100; Height = 740; MinWidth = 700; MinHeight = 500;
        var root = new DockPanel { Margin = new(12) };
        explanation = new() { Text = proposal.Summary, TextWrapping = TextWrapping.Wrap };
        var disclosure = new ScrollViewer { Content = explanation, MaxHeight = 220 };
        DockPanel.SetDock(disclosure, Dock.Top);
        root.Children.Add(disclosure);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new(0, 10) };
        var confirm = new Button { Content = "Adopt latest baseline only" };
        AutomationProperties.SetName(confirm, "Adopt exact latest baseline in memory; preserve raw draft; no Save or use approval");
        confirm.Click += (_, _) =>
        {
            if (current is null) { return; }
            if (Current()) { Close(true); } else { Withdraw(); }
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => { if (current is not null) { Close(); } };
        buttons.Children.Add(confirm); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var panes = new Grid { ColumnDefinitions = new("*,*,*") };
        AddBody(panes, 0, "Frozen old inspected baseline", proposal.Original.Document.Text);
        AddBody(panes, 1, "Frozen latest explicitly observed source", proposal.Latest.Document.Text);
        AddBody(panes, 2, "Exact raw unsaved draft; not merged or prepared Save bytes", proposal.Draft.Draft);
        root.Children.Add(panes);
        Content = root;
        AddHandler(TextBox.CopyingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AddHandler(TextBox.CuttingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AddHandler(TextBox.PastingFromClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, BlockTransferKeys, RoutingStrategies.Tunnel);
        timer.Tick += OnTick;
        Opened += (_, _) => { if (Current()) { timer.Start(); } else { Withdraw(); } };
        Closed += (_, _) => Retire();
        KeyDown += (_, key) => { if (key.Key == Key.Escape) { key.Handled = true; Close(); } };
    }

    private bool Current() => current?.Invoke() == true;
    private void OnTick(object? sender, EventArgs args) { if (!Current()) { Withdraw(); } }

    internal Task Withdrawal => withdrawal.Task;

    internal void Withdraw()
    {
        Retire();
        if (withdrawalQueued) { return; }
        withdrawalQueued = true;
        // Avalonia can finish making a window visible after a reentrant close in Opened.
        Dispatcher.UIThread.Post(() => { Hide(); Close(); withdrawal.TrySetResult(); });
    }

    private void AddBody(Grid panes, int column, string label, string text)
    {
        var pane = new DockPanel { Margin = new(4) };
        var title = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(title, Dock.Top);
        pane.Children.Add(title);
        var body = new TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new("Consolas"), ContextMenu = null, IsUndoEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(body, label);
        bodies.Add(body);
        pane.Children.Add(body);
        Grid.SetColumn(pane, column);
        panes.Children.Add(pane);
    }

    private static void BlockTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;

    private static void BlockTransferKeys(object? sender, KeyEventArgs args)
    {
        var control = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != KeyModifiers.None;
        var shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((control && args.Key is Key.C or Key.X or Key.V or Key.Insert)
            || (shift && args.Key is Key.Insert or Key.Delete) || args.Key == Key.Apps || (shift && args.Key == Key.F10))
        { args.Handled = true; }
    }

    internal void Retire()
    {
        timer.Stop();
        timer.Tick -= OnTick;
        current = null;
        foreach (var body in bodies) { body.Text = string.Empty; }
        explanation.Text = string.Empty;
        Content = null;
    }
}
