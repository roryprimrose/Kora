using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed class ComposerRequestReviewPanel : StackPanel
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public ComposerRequestReviewPanel()
    {
        Spacing = 6;
        var review = new Button { Name = "ReviewComposerRequest", Content = "Review unsent message once" };
        review.Bind(IsEnabledProperty, new Binding(nameof(SessionsViewModel.CanReviewComposerRequest)));
        BindAction(review, model => model.CaptureComposerRequestReview());
        Children.Add(review);
        var text = new TextBox { Name = "ComposerRequestReview", IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 260 };
        text.Bind(TextBox.TextProperty, new Binding(nameof(SessionsViewModel.ComposerRequestReviewText)));
        AutomationProperties.SetName(text, "Complete unchanged unsent message, original conversation, local-only scope, channel and expiry");
        Children.Add(text);
        var confirm = new Button { Name = "ConfirmComposerRequest", Content = "Confirm and submit reviewed message once" };
        confirm.Bind(IsEnabledProperty, new Binding(nameof(SessionsViewModel.CanConfirmComposerRequest)));
        BindAction(confirm, model => model.CaptureComposerRequestDecision(true));
        var cancel = new Button { Name = "CancelComposerRequest", Content = "Cancel review; keep unsent message" };
        cancel.Bind(IsEnabledProperty, new Binding(nameof(SessionsViewModel.CanCancelComposerRequest)));
        BindAction(cancel, model => model.CaptureComposerRequestDecision(false));
        Children.Add(new WrapPanel { Children = { confirm, cancel } });
    }

    private void BindAction(Button button, Func<SessionsViewModel, Func<Task>> capture)
    {
        Func<Task>? captured = null;
        Func<Task>? Capture() => DataContext is SessionsViewModel model ? capture(model) : null;
        button.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => captured = Capture(), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter) { captured = Capture(); }
        }, RoutingStrategies.Tunnel);
        button.Click += async (_, _) =>
        {
            var action = captured ?? Capture();
            captured = null;
            if (action is not null) { await (Operation = action()); }
        };
    }
}
