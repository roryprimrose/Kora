using Kora.Core.Skills;

namespace Kora.Windows.Skills.RuntimeSetup;

public sealed record BundledRuntimeInspection(
    BundledRuntimeInstallation? Installation, BundledRuntimeRegistration? Registration, bool InstallationInspected = true,
    BundledRuntimeRegistration? PendingRegistration = null)
{
    internal BundledRuntimeRegistration? EffectiveRegistration => PendingRegistration ?? Registration;
    public BundledRuntimePreparation Preparation => !InstallationInspected ? BundledRuntimePreparation.Uninspected : EffectiveRegistration is null
        ? Installation is null ? BundledRuntimePreparation.Missing : BundledRuntimePreparation.Unprepared
        : Installation is null ? BundledRuntimePreparation.Changed
        : !string.Equals(EffectiveRegistration.DeviceDigest, Installation.DeviceDigest, StringComparison.Ordinal) ? BundledRuntimePreparation.OtherDevice
        : !string.Equals(EffectiveRegistration.InventoryDigest, Installation.InventoryDigest, StringComparison.Ordinal) ? BundledRuntimePreparation.Changed
        : PendingRegistration is null && EffectiveRegistration.Confirmed ? BundledRuntimePreparation.Prepared : BundledRuntimePreparation.Unconfirmed;

    public string Description => $"{Preparation}. " + (Preparation == BundledRuntimePreparation.Prepared
        ? "The exact protected local installation was re-read and matches the reviewed inventory. "
        : "Inspect and review the exact current installation before preparing or reconciling its registration. ")
        + BundledRuntimeQualification.Requirements;
}
