using Microsoft.Win32;

namespace Kora.Setup;

internal static class WindowsStartupRegistrationProbe
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static InstallerStartupStatus Inspect(InstallScope scope, string productVersion)
    {
        var expected = $"\"{WindowsInstalledApplicationPaths.Executable(scope, productVersion)}\"";
        using var hive = RegistryKey.OpenBaseKey(
            scope == InstallScope.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
        using var run = hive.OpenSubKey(RunKey, writable: false);
        var command = ReadCommand(run);
        using var approved = hive.OpenSubKey(ApprovalKey, writable: false);
        var approval = ReadApproval(approved);
        return InstallerStartupStatus.Inspect(command, approval, expected);
    }

    private static string? ReadCommand(RegistryKey? key)
    {
        var value = key?.GetValue("Kora", defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) { return null; }
        if (key!.GetValueKind("Kora") != RegistryValueKind.String || value is not string command)
        {
            throw new InvalidDataException("Kora's startup entry has an unsupported registry type.");
        }
        return command;
    }

    private static byte[] ReadApproval(RegistryKey? key)
    {
        var value = key?.GetValue("Kora", defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) { return []; }
        if (key!.GetValueKind("Kora") != RegistryValueKind.Binary || value is not byte[] bytes)
        {
            throw new InvalidDataException("Kora's Windows startup approval entry has an unsupported registry type.");
        }
        return bytes;
    }

}
