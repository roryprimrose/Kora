namespace Kora.Windows.Coordination;

internal static class InstallerMaintenanceMarkerFormat
{
    internal static ReadOnlySpan<byte> Identity(InstallerContinuityMarkerKind kind) => kind switch
    {
        InstallerContinuityMarkerKind.Owner => "Kora.InstallerMaintenance.v1.owner\n"u8,
        InstallerContinuityMarkerKind.DebugRecoveryGuard => "Kora.InstallerMaintenance.v1.guard\n"u8,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
