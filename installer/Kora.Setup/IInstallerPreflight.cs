namespace Kora.Setup;

public interface IInstallerPreflight
{
    Task<InstallerPreflightResult> ProbeAsync(CancellationToken cancellationToken);
}
