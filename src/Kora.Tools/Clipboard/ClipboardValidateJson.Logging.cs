using Microsoft.Extensions.Logging;

namespace Kora.Tools.Clipboard;

public sealed partial class ClipboardValidateJson
{
    [LoggerMessage(EventId = 746, Level = LogLevel.Error,
        Message = "Native clipboard JSON syntax validation failed internally; no syntax result or source-use authority claimed")]
    private static partial void Failure(ILogger logger);
}
