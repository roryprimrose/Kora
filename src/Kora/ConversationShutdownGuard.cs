using Kora.Application.ViewModels;

namespace Kora;

internal static class ConversationShutdownGuard
{
    internal static void Bind(MainViewModel main, NewRequestWindowController requests, SessionsWindowController sessions,
        SessionRoutingController? routing = null, EvidenceWindowController? evidence = null)
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
            if (evidence is not null) { await evidence.PrepareShutdownAsync(); }
            var evidenceCurrent = evidence?.CaptureShutdownEligibility() ?? (static () => true);
            current = () => routingCurrent() && requestCurrent() && sessionsCurrent() && evidenceCurrent();
            return current();
        }, () => current?.Invoke() == true, () =>
        {
            var requestsCurrent = requests.CaptureShutdownEligibility(exitWithoutDisclosure: true);
            var sessionsCurrent = sessions.CaptureShutdownEligibility(exitWithoutDisclosure: true);
            var routingCurrent = routing?.CaptureShutdownEligibility() ?? (static () => true);
            var evidenceCurrent = evidence?.CaptureShutdownEligibility() ?? (static () => true);
            return () => requestsCurrent() && sessionsCurrent() && routingCurrent() && evidenceCurrent();
        });
    }
}
