using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Tools;

internal static partial class CapabilityLog
{
    [LoggerMessage(1, LogLevel.Information, "Read-only capability {CapabilityId} returned {CapabilityOutcome} ({CapabilityReason}).")]
    public static partial void Result(ILogger logger, string capabilityId, CapabilityOutcome capabilityOutcome, string capabilityReason);
}
