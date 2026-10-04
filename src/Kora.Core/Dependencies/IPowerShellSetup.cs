namespace Kora.Core.Dependencies;

public interface IPowerShellSetup : ISetupDependencyProbe
{
    Task InstallAsync(CancellationToken cancellationToken);
}
