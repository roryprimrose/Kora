using Microsoft.Extensions.Logging;

namespace Kora.Tools.Clipboard;

public sealed partial class ClipboardInspectWebUrl
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Native clipboard URL syntax inspection failed; no result published.")]
    private static partial void InspectionFailed(ILogger logger);
}
