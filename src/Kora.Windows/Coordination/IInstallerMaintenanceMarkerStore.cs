namespace Kora.Windows.Coordination;

/// <summary>Creates a new, durable marker without replacing an existing filesystem object.</summary>
internal interface IInstallerMaintenanceMarkerStore
{
    /// <summary>Returns an owned handle only after the marker's maintenance identity is durably flushed.</summary>
    IInstallerMaintenanceMarker CreateDurable(InstallerContinuityMarkerKind kind);
}
