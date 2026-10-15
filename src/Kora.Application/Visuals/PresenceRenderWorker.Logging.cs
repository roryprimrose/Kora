using Microsoft.Extensions.Logging;

namespace Kora.Application.Visuals;

public sealed partial class PresenceRenderWorker
{
    [LoggerMessage(0, LogLevel.Error, "Native presence rendering failed; the presenter is unavailable.")]
    private partial void RenderFailed(Exception exception);
}