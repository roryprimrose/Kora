using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    [LoggerMessage(0, LogLevel.Information, "Presence renderer replacement completed; native renderer {NativeRenderer}")]
    private static partial void RendererApplied(ILogger<App> logger, bool nativeRenderer);

    [LoggerMessage(1, LogLevel.Error, "Presence renderer replacement failed; automatic fallback was not performed")]
    private static partial void RendererSwitchFailed(ILogger<App> logger, Exception exception);
}
