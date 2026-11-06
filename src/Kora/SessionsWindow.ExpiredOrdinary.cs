using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindExpiredOrdinaryDecision(Button button, bool confirm, SessionsViewModel model)
    {
        Func<Task>? captured = null;
        button.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => captured = model.CaptureExpiredOrdinaryDecision(confirm), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter) { captured = model.CaptureExpiredOrdinaryDecision(confirm); }
        }, RoutingStrategies.Tunnel);
        button.Click += async (_, _) =>
        {
            var decision = captured ?? model.CaptureExpiredOrdinaryDecision(confirm);
            captured = null;
            await (ConversationOperation = decision());
        };
    }
}
