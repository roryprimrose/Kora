namespace Kora.Setup;

public enum InstallerDependencyState
{
    Checking,
    Installed,
    Missing,
    UpdateRequired,
    Detected,
    NotRunning,
    NeedsPreparation,
    Incompatible,
    Failed,
}
