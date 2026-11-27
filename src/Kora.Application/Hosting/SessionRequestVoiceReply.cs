using Kora.Application.Hosting.SessionRouting;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public static class SessionRequestVoiceReply
{
    public static SessionRoutingVoiceReply Create(SessionRoutingVoiceRequest request, string chat,
        Func<bool> exact, Func<QuestionVoiceAdmission?> observe,
        Func<string, Func<bool>, CancellationToken, Task> speak, ILogger logger)
    {
        if (request.Request.Origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Automatic send replies require the exact original activated voice request."); }
        return new(request, exact, observe,
            (eligible, token) => speak("Send to " + chat + ": " + request.Body + "?", eligible, token), logger);
    }
}
