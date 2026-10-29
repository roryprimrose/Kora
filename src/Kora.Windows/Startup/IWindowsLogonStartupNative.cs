namespace Kora.Windows.Startup;

/// <summary>Windows installation/registry seam; deterministic tests never use the real registry.</summary>
internal interface IWindowsLogonStartupNative
{
    InstalledLogonApplication? InspectInstallation();
    string? ReadUserCommand();
    string? ReadMachineCommand();
    byte[] ReadUserApproval();
    void WriteUserCommand(InstalledLogonApplication installation, string? expected, string? replacement, Func<bool> admitted);
}
