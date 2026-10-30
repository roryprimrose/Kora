using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed class TaskResponseOutputPanel : StackPanel
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public TaskResponseOutputPanel()
    {
        Spacing = 6;
        Children.Add(new TextBlock { Text = "Response output - this new task only" });
        var choices = new ComboBox { Name = "TaskOutputMode" };
        choices.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SessionsViewModel.TaskOutputModes)));
        choices.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(SessionsViewModel.TaskOutputMode)) { Mode = BindingMode.TwoWay });
        choices.Bind(IsEnabledProperty, new Binding(nameof(SessionsViewModel.CanReviewTaskOutput)));
        AutomationProperties.SetName(choices, "Output for this one new task only; Inherit, VisualOnly, Hybrid or VoiceOnly");
        Children.Add(choices);
        var review = new Button { Name = "ReviewTaskOutput", Content = "Review this task's input and output" };
        review.Bind(IsEnabledProperty, new Binding(nameof(SessionsViewModel.CanReviewTaskOutput)));
        review.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ReviewTaskOutputAsync()); }
        };
        Children.Add(review);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 220 };
        text.Bind(TextBox.TextProperty, new Binding(nameof(SessionsViewModel.TaskOutputReviewText)));
        AutomationProperties.SetName(text, "Exact one-task output review with complete unchanged original input");
        Children.Add(text);
        var confirm = new Button { Name = "ConfirmTaskOutput", Content = "Confirm and submit this task once" };
        confirm.Bind(IsEnabledProperty, new Binding(nameof(SessionsViewModel.CanConfirmTaskOutput)));
        var cancel = new Button { Name = "CancelTaskOutput", Content = "Cancel this task review (no submission)" };
        BindDecision(confirm, true);
        BindDecision(cancel, false);
        Children.Add(new WrapPanel { Children = { confirm, cancel } });
    }

    private void BindDecision(Button button, bool confirm)
    {
        Func<Task>? captured = null;
        button.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => captured = (DataContext as SessionsViewModel)?.CaptureTaskOutputDecision(confirm), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter) { captured = (DataContext as SessionsViewModel)?.CaptureTaskOutputDecision(confirm); }
        }, RoutingStrategies.Tunnel);
        button.Click += async (_, _) =>
        {
            var decision = captured ?? (DataContext as SessionsViewModel)?.CaptureTaskOutputDecision(confirm);
            captured = null;
            if (decision is not null) { await (Operation = decision()); }
        };
    }
}
