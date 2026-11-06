using Kora.Windows.Coordination;

namespace Kora.Setup;

internal sealed class WindowsInstallerShutdown : IInstallerShutdown
{
    public async Task<IInstallerMaintenanceLease> AcquireAsync(InstallScope scope, CancellationToken cancellationToken)
        => await WindowsInstallerMaintenanceLease.AcquireAsync(
            scope == InstallScope.AllUsers, cancellationToken).ConfigureAwait(false);

    public void Validate(InstallScope scope)
        => WindowsInstallerMaintenanceLease.ValidateUserSession(scope == InstallScope.AllUsers);
}
