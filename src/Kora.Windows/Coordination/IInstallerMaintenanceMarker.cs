namespace Kora.Windows.Coordination;

/// <summary>Owns the exact object created for one reservation, independently of later path replacement.</summary>
internal interface IInstallerMaintenanceMarker : IDisposable
{
    /// <summary>Requests deletion of only the object held by this handle.</summary>
    void DeleteOwned();
}
