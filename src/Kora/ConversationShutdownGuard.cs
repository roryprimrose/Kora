using Kora.Application.ViewModels;

namespace Kora;

internal static class ConversationShutdownGuard
{
    internal static void Bind(MainViewModel main, NewRequestWindowController requests, SessionsWindowController sessions,
        SessionRoutingController? routing = null)
    {
        Func<bool>? current = null;
        main.BindConversationShutdown(async () =>
        {
            current = null;
            if (routing is not null && !await routing.PrepareShutdown()) { return false; }
            var routingCurrent = routing?.CaptureShutdownEligibility() ?? (static () => true);
            if (!await requests.PrepareShutdown()) { return false; }
            var requestCurrent = requests.CaptureShutdownEligibility();
            if (!await sessions.PrepareShutdown()) { return false; }
            var sessionsCurrent = sessions.CaptureShutdownEligibility();
            current = () => routingCurrent() && requestCurrent() && sessionsCurrent();
            return current();
        }, () => current?.Invoke() == true);
    }
}
