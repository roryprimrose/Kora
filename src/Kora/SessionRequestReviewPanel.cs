using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Application.ViewModels;

namespace Kora;

internal sealed class SessionRequestReviewPanel : StackPanel
{
    private MainViewModel? main;
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public SessionRequestReviewPanel()
    {
        Spacing = 8;
        Bind(IsVisibleProperty, new Binding(nameof(MainViewModel.HasSessionOperationReview)));
        var heading = new TextBlock { Text = "Review original request - nothing sent yet" };
        heading.Bind(IsVisibleProperty, new Binding(nameof(MainViewModel.HasForegroundRequestReview)));
        Children.Add(heading);
        var body = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxHeight = 260 };
        AutomationProperties.SetName(body, "Complete original request, readable conversation target, local provider, original channel and expiry");
        body.Bind(TextBox.TextProperty, new Binding(nameof(MainViewModel.ForegroundRequestReviewText)));
        body.Bind(IsVisibleProperty, new Binding(nameof(MainViewModel.HasForegroundRequestReview)));
        Children.Add(body);
        var buttons = new WrapPanel();
        var confirm = new Button { Name = "ConfirmOriginalRequest", Content = "_Confirm original request once" };
        var cancel = new Button { Name = "CancelOriginalRequest", Content = "_Cancel original request" };
        BindDecision(confirm, confirmed: true, pendingOnly: false);
        BindDecision(cancel, confirmed: false, pendingOnly: false);
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        buttons.Bind(IsVisibleProperty, new Binding(nameof(MainViewModel.HasForegroundRequestReview)));
        Children.Add(buttons);
        var pending = new StackPanel { Spacing = 8 };
        pending.Bind(IsVisibleProperty, new Binding(nameof(MainViewModel.HasPendingClearReview)));
        var pendingBody = new TextBox { IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 320 };
        AutomationProperties.SetName(pendingBody, "Complete ordinary pending-only clear identities, positions, count, impact, original channel and expiry");
        pendingBody.Bind(TextBox.TextProperty, new Binding(nameof(MainViewModel.PendingClearReviewText)));
        pending.Children.Add(pendingBody);
        var pendingButtons = new WrapPanel();
        var clear = new Button { Name = "ConfirmPendingOnlyClear", Content = "Confirm clear pending only" };
        var discard = new Button { Name = "CancelPendingOnlyClear", Content = "Cancel pending-only review" };
        BindDecision(clear, confirmed: true, pendingOnly: true);
        BindDecision(discard, confirmed: false, pendingOnly: true);
        pendingButtons.Children.Add(clear);
        pendingButtons.Children.Add(discard);
        pending.Children.Add(pendingButtons);
        Children.Add(pending);
    }

    internal void Bind(MainViewModel? value)
    {
        main = value;
        DataContext = value;
    }

    private void BindDecision(Button button, bool confirmed, bool pendingOnly)
    {
        Func<Task>? captured = null;
        Func<Task>? Capture() => pendingOnly ? main?.CapturePendingClearDecision(confirmed) : main?.CaptureForegroundRequestDecision(confirmed);
        button.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => captured = Capture(), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter) { captured = Capture(); }
        }, RoutingStrategies.Tunnel);
        button.Click += (_, _) =>
        {
            var decision = captured ?? Capture();
            captured = null;
            Operation = Run(decision);
        };
    }

    private async Task Run(Func<Task>? decision)
    {
        var host = main;
        if (host is null || decision is null) { return; }
        try
        {
            await decision();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { host.ReportHostInteractionFailure("Original request review failed. Inspect the exact session before retry. Failure type: " + exception.GetType().Name); }
    }
}
