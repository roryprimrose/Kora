using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SkillPackagesWindowController
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Embedded skill inspection denied: {ReasonCode}")]
    private static partial void InspectionDenied(ILogger logger, string reasonCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Embedded skill inspection admitted: {PackageCount} packages, {InvocationAvailable}")]
    private static partial void InspectionAdmitted(ILogger logger, int packageCount, bool invocationAvailable);

    [LoggerMessage(Level = LogLevel.Error, Message = "Embedded skill inspection failed: {ReasonCode}")]
    private static partial void InspectionFailed(ILogger logger, Exception exception, string reasonCode);
}
