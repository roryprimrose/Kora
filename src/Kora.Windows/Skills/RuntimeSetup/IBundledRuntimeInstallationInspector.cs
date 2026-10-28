namespace Kora.Windows.Skills.RuntimeSetup;

internal interface IBundledRuntimeInstallationInspector
{
    Task<BundledRuntimeInstallation?> InspectAsync(CancellationToken cancellationToken);
}
