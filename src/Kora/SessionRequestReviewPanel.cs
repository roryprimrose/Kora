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
        Bind(IsVisibleProperty, new Binding(nameof(MainViewModel.HasForegroundRequestReview)));
        Children.Add(new TextBlock { Text = "Review original request - nothing sent yet" });
        var body = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxHeight = 260 };
        AutomationProperties.SetName(body, "Complete original request, readable conversation target, local provider, original channel and expiry");
        body.Bind(TextBox.TextProperty, new Binding(nameof(MainViewModel.ForegroundRequestReviewText)));
        Children.Add(body);
        var buttons = new WrapPanel();
        var confirm = new Button { Name = "ConfirmOriginalRequest", Content = "_Confirm original request once" };
        var cancel = new Button { Name = "CancelOriginalRequest", Content = "_Cancel original request" };
        BindDecision(confirm, confirmed: true);
        BindDecision(cancel, confirmed: false);
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        Children.Add(buttons);
    }

    internal void Bind(MainViewModel? value)
    {
        main = value;
        DataContext = value;
    }

    private void BindDecision(Button button, bool confirmed)
    {
        Func<Task>? captured = null;
        button.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => captured = main?.CaptureForegroundRequestDecision(confirmed), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter) { captured = main?.CaptureForegroundRequestDecision(confirmed); }
        }, RoutingStrategies.Tunnel);
        button.Click += (_, _) =>
        {
            var decision = captured ?? main?.CaptureForegroundRequestDecision(confirmed);
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
