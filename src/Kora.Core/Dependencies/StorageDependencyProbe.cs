using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Core.Dependencies;

public sealed class StorageDependencyProbe(
    IApplicationDataPaths paths,
    ILogger<StorageDependencyProbe> logger) : IDependencyProbe
{
    public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(paths.LocalRoot);
        Directory.CreateDirectory(Path.Combine(paths.LocalRoot, "Logs"));
        Directory.CreateDirectory(Path.Combine(paths.RoamingRoot, "Skills"));
        CoreLog.StorageReady(logger);

        return ValueTask.FromResult(new DependencyStatus(
            "kora.storage",
            "Application storage",
            DependencyReadiness.Ready,
            $"Local data is ready at {paths.LocalRoot}."));
    }
}