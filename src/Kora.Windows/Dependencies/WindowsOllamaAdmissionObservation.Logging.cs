using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed partial class WindowsOllamaAdmissionObservation
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Local model admission observation unavailable: {Reason}. Inspect the exact approved runtime and model; review again after recovery.")]
    private static partial void ObservationUnavailable(ILogger logger, string reason);
}