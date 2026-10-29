using Microsoft.Extensions.Logging;

namespace Kora.Windows.Skills.RuntimeSetup;

public sealed partial class WindowsBundledRuntimeSetup
{
    [LoggerMessage(7830, LogLevel.Warning, "Bundled runtime installation inspection failed; no readiness inferred.")]
    private static partial void InspectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(7831, LogLevel.Warning, "Bundled runtime registration failed. Reconciliation required: {Unconfirmed}")]
    private static partial void SetupFailed(ILogger logger, Exception exception, bool unconfirmed);
}
