using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora.Core.Diagnostics;

internal static partial class CoreLog
{
    [LoggerMessage(1, LogLevel.Debug, "Starting {ProbeCount} dependency probes.")]
    public static partial void DependencyProbesStarting(ILogger logger, int probeCount);

    [LoggerMessage(2, LogLevel.Information, "Dependency {DependencyId} reported {Readiness}.")]
    public static partial void DependencyProbeCompleted(
        ILogger logger,
        string dependencyId,
        DependencyReadiness readiness);

    [LoggerMessage(3, LogLevel.Debug, "Completed all dependency probes.")]
    public static partial void DependencyProbesCompleted(ILogger logger);

    [LoggerMessage(4, LogLevel.Information, "Application storage directories are ready.")]
    public static partial void StorageReady(ILogger logger);
}
