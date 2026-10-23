using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class StaticHtmlDetailRenderer
{
    [LoggerMessage(316, LogLevel.Warning,
        "Static HTML source fallback: {Reason}, ItemId {ItemId}, Revision {Revision}, Profile {Profile}.")]
    private static partial void RenderFallback(ILogger logger, string reason, Guid itemId, long revision, string profile);
}
