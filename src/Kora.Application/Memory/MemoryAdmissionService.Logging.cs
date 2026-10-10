using Kora.Core.Memory;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Memory;

internal sealed partial class MemoryAdmissionService
{
    [LoggerMessage(1790, LogLevel.Information,
        "Memory operation {MemoryOperation}: {MemoryOutcome}; reason {MemoryReason}; identity {MemoryId}.")]
    private static partial void Result(ILogger logger, MemoryOperation memoryOperation,
        MemoryOutcome memoryOutcome, MemoryReason memoryReason, Guid? memoryId);

    [LoggerMessage(1791, LogLevel.Warning, "Memory admission failed with {FailureType}; outcome unknown.")]
    private static partial void Failure(ILogger logger, string failureType);
}
