using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration.SettingInspection;

public sealed partial class SettingInspectionWorkflow
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Setting inspection failed: {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
