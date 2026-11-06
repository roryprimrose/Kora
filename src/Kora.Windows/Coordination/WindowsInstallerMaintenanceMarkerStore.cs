namespace Kora.Windows.Coordination;

internal sealed class WindowsInstallerMaintenanceMarkerStore(string markerPath, string sid) : IInstallerMaintenanceMarkerStore
{
    public IInstallerMaintenanceMarker CreateDurable(InstallerContinuityMarkerKind kind)
    {
        var path = kind switch
        {
            InstallerContinuityMarkerKind.Owner => markerPath,
            InstallerContinuityMarkerKind.DebugRecoveryGuard => markerPath + ".debug-recovery",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The maintenance marker directory is unknown."));
        var file = CoordinationNative.CreateInstallerContinuityFile(path, sid);
        try
        {
            file.Write(InstallerMaintenanceMarkerFormat.Identity(kind));
            file.Flush(flushToDisk: true);
            return new Marker(file);
        }
        catch
        {
            // A partial/uncertain write remains a dirty marker, never a recovered clean state.
            file.Dispose();
            throw;
        }
    }

    private sealed class Marker(FileStream file) : IInstallerMaintenanceMarker
    {
        public void DeleteOwned() => CoordinationNative.DeleteInstallerContinuityFile(file.SafeFileHandle);
        public void Dispose() => file.Dispose();
    }
}
