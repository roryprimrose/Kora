namespace Kora.Windows.Skills.RuntimeSetup;

internal static class BundledRuntimeQualification
{
    internal const string Requirements =
        "Execution and grants unavailable: this release has no admitted contained PowerShell profile. "
        + "W1 attributable network/filesystem denial, W2 registered dependency/module and child enforcement, "
        + "W3 fixed bridge/native-effect qualification, W4 protected installed deployment and exact platform evidence remain unconfirmed. "
        + "A signature, inventory match or saved registration is not that evidence. No worker, script or native power effect was started.";

    // Release-owned admissions only. User preferences, artifact text and setup observations cannot populate this set.
    private static readonly IReadOnlyList<Admission> Admissions = Array.Empty<Admission>();

    internal static BundledWorkerProfile? Resolve(BundledRuntimeInspection inspection) =>
        inspection.Preparation == BundledRuntimePreparation.Prepared && inspection.Installation is { } installed
            ? Admissions.SingleOrDefault(admission => string.Equals(admission.InventoryDigest, installed.InventoryDigest, StringComparison.Ordinal)
                && string.Equals(admission.DeviceDigest, installed.DeviceDigest, StringComparison.Ordinal))?.Profile
            : null;

    private sealed record Admission(string InventoryDigest, string DeviceDigest, BundledWorkerProfile Profile);
}
