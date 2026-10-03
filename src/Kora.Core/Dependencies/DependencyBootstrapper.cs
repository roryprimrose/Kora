namespace Kora.Core.Dependencies;

public sealed class DependencyBootstrapper(IEnumerable<IDependencyProbe> probes)
{
    private readonly IDependencyProbe[] probes = probes.ToArray();

    public async Task<IReadOnlyList<DependencyStatus>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DependencyStatus>(probes.Length);

        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await probe.ProbeAsync(cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}