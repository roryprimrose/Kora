using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;
namespace Kora.Core.Dependencies;

public sealed class DependencyBootstrapper(
    IEnumerable<IDependencyProbe> probes,
    ILogger<DependencyBootstrapper> logger)
{
    private readonly IDependencyProbe[] probes = probes.ToArray();

    public async Task<IReadOnlyList<DependencyStatus>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        CoreLog.DependencyProbesStarting(logger, probes.Length);
        var results = new List<DependencyStatus>(probes.Length);

        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            CoreLog.DependencyProbeCompleted(
                logger,
                result.Id,
                result.Readiness);
            results.Add(result);
        }

        CoreLog.DependencyProbesCompleted(logger);
        return results;
    }
}