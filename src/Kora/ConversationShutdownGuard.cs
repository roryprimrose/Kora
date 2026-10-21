using Kora.Application.ViewModels;

namespace Kora;

internal static class ConversationShutdownGuard
{
    internal static void Bind(MainViewModel main, NewRequestWindowController requests, SessionsWindowController sessions)
    {
        Func<bool>? current = null;
        main.BindConversationShutdown(async () =>
        {
            current = null;
            if (!await requests.PrepareShutdown()) { return false; }
            var requestCurrent = requests.CaptureShutdownEligibility();
            if (!await sessions.PrepareShutdown()) { return false; }
            var sessionsCurrent = sessions.CaptureShutdownEligibility();
            current = () => requestCurrent() && sessionsCurrent();
            return current();
        }, () => current?.Invoke() == true);
    }
}
