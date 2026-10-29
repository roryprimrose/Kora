using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindLocalRequestProfileDecision(Button button, bool confirm, SessionsViewModel model)
    {
        Func<Task>? captured = null;
        button.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => captured = model.CaptureLocalRequestProfileDecision(confirm), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter) { captured = model.CaptureLocalRequestProfileDecision(confirm); }
        }, RoutingStrategies.Tunnel);
        button.Click += async (_, _) =>
        {
            var decision = captured ?? model.CaptureLocalRequestProfileDecision(confirm);
            captured = null;
            await (ConversationOperation = decision());
        };
    }
}
