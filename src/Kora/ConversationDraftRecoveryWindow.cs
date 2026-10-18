using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Presentation;
using Kora.Controls;

namespace Kora;

internal sealed class ConversationDraftRecoveryWindow : Window
{
    private readonly TaskCompletionSource<bool> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SnapshotComparisonFindPane[] panes;
    private readonly NamedTextBlock explanation;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Func<bool>? current;
    private bool opening = true;

    private ConversationDraftRecoveryWindow(Window owner, ConversationDraftRecoveryReview review)
    {
        current = () => owner.IsVisible && review.Available;
        Title = "Review saved conversation draft recovery";
        Width = 1200; Height = 760; MinWidth = 760; MinHeight = 540;
        var root = new DockPanel { Margin = new(12) };
        explanation = new() { Text = review.Summary, TextWrapping = TextWrapping.Wrap };
        var disclosure = new ScrollViewer { Content = explanation, MaxHeight = 180 };
        DockPanel.SetDock(disclosure, Dock.Top);
        root.Children.Add(disclosure);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new(0, 10) };
        var adopt = new Button { Content = "Adopt latest baseline only" };
        AutomationProperties.SetName(adopt, "Confirm exact latest saved baseline in memory; raw composer unchanged; no Save or Submit");
        adopt.Click += (_, _) => Finish(!opening && Current());
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Finish(false);
        buttons.Children.Add(adopt); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        panes =
        [
            new("Old observed saved body; absent is not empty", review.OldBody, Current, Withdraw),
            new("Latest explicitly observed saved body; absent is not empty", review.LatestBody, Current, Withdraw),
            new("Exact raw unsaved composer memory buffer; no merge", review.Buffer, Current, Withdraw),
        ];
        var sources = new Grid { ColumnDefinitions = new("*,*,*") };
        for (var index = 0; index < panes.Length; index++)
        {
            Grid.SetColumn(panes[index], index);
            sources.Children.Add(panes[index]);
        }
        root.Children.Add(sources);
        Content = root;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Finish(false); }
        };
        timer.Tick += OnTick;
        Closed += (_, _) =>
        {
            Erase();
            reply.TrySetResult(false);
        };
    }

    internal static async Task<bool> ReviewAsync(Window owner, SessionsViewModel model,
        ConversationDraftRecoveryReview review, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!owner.IsVisible || !review.Available) { throw new OperationCanceledException("The draft recovery surface expired."); }
        var dialog = new ConversationDraftRecoveryWindow(owner, review);
        EventHandler retire = (_, _) => dialog.Withdraw();
        model.DraftRecoveryRetired += retire;
        owner.Closed += retire;
        using var registration = token.Register(() => Dispatcher.UIThread.Post(dialog.Withdraw));
        try
        {
            dialog.Show(owner);
            dialog.opening = false;
            if (!dialog.Current() || token.IsCancellationRequested) { dialog.Withdraw(); }
            else { dialog.timer.Start(); }
            return await dialog.reply.Task;
        }
        finally
        {
            model.DraftRecoveryRetired -= retire;
            owner.Closed -= retire;
            dialog.Erase();
        }
    }

    private bool Current() => current?.Invoke() == true;
    private void OnTick(object? sender, EventArgs args) { if (!Current()) { Withdraw(); } }

    private void Finish(bool adopt)
    {
        reply.TrySetResult(adopt);
        Erase();
        if (!opening) { Close(); }
    }

    private void Withdraw() => Finish(false);

    private void Erase()
    {
        timer.Stop();
        timer.Tick -= OnTick;
        current = null;
        foreach (var pane in panes) { pane.Dispose(); }
        explanation.Text = string.Empty;
        Content = null;
    }
}
