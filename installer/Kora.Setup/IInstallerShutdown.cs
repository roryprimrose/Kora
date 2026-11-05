using Kora.Windows.Coordination;

namespace Kora.Setup;

/// <summary>Obtains exclusive package-maintenance admission after confirmed host shutdown.</summary>
public interface IInstallerShutdown
{
    /// <summary>Retains the lifecycle reservation until package work and rollback have terminated.</summary>
    Task<IInstallerMaintenanceLease> AcquireAsync(InstallScope scope, CancellationToken cancellationToken);

    /// <summary>Revalidates interactive/scope eligibility immediately before native apply.</summary>
    void Validate(InstallScope scope);
}
