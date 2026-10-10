using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    [LoggerMessage(378, LogLevel.Debug,
        "Passive work overview observed; scanned {Scanned}, groups {Groups}, nonmatches {Nonmatches}, unavailable {Unavailable}, output limited {OutputLimited}.")]
    private static partial void OverviewRead(ILogger logger,
        int scanned, int groups, int nonmatches, int unavailable, bool outputLimited);

    [LoggerMessage(182, LogLevel.Error, "Sessions workspace failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
