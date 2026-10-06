namespace Kora.Setup;

public interface IInstallerApplicationLauncher
{
    Task LaunchAsync(InstallScope scope, CancellationToken cancellationToken);
}
