namespace Kora.Core.Dependencies;

public interface IDependencyProbe
{
    ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken);
}