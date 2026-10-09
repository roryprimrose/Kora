using Microsoft.Extensions.Logging;

namespace Kora.Application.Skills;

public sealed partial class SharedSkillDiscoveryService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Read-only shared skill source registration saved: {SourceCount}")]
    private static partial void RegistrationSaved(ILogger logger, int sourceCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local shared skill inspection completed: {PackageCount}")]
    private static partial void DiscoveryCompleted(ILogger logger, int packageCount);
}
