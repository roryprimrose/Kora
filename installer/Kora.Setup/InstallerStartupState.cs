namespace Kora.Setup;

public enum InstallerStartupState
{
    Checking,
    NotRegistered,
    Enabled,
    DisabledByWindows,
    Conflict,
    Failed,
}
