using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Kora.Windows.Startup;

internal sealed partial class WindowsLogonStartupNative : IWindowsLogonStartupNative
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string UpgradeCode = "{9F375B94-FE7C-4B0E-B74F-A885D889A03C}";

    public InstalledLogonApplication? InspectInstallation()
    {
        if (!Environment.Is64BitProcess || Environment.ProcessPath is not { } executable
            || !string.Equals(Path.GetFileName(executable), "Kora.exe", StringComparison.Ordinal)
            || Path.GetPathRoot(executable) is not { Length: 3 } root
            || new DriveInfo(root).DriveType != DriveType.Fixed)
        { return null; }
        var directory = Path.GetDirectoryName(executable)!;
        RejectLinks(directory);
        using var exe = OpenImage(executable);
        using var dll = OpenImage(Path.Combine(directory, "Kora.dll"));
        return ResolveInstalled(executable, exe, dll);
    }

    private static FileStream OpenImage(string path)
    {
        RejectLinks(path);
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void RejectLinks(string path)
    {
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
        {
            if (File.GetAttributes(item.FullName).HasFlag(FileAttributes.ReparsePoint))
            { throw new InvalidDataException("Startup installation contains a redirected path."); }
        }
    }

    private static unsafe InstalledLogonApplication? ResolveInstalled(string executable, FileStream exe, FileStream dll)
    {
        var product = stackalloc char[39];
        for (uint index = 0; index < 100; index++)
        {
            var result = MsiEnumRelatedProducts(UpgradeCode, 0, index, product);
            if (result == 259) { return null; }
            if (result != 0) { throw new IOException("Windows Installer product enumeration failed: " + result); }
            var code = new string(product);
            var location = ProductProperty(code, "InstallLocation");
            if (location is null || !Path.IsPathFullyQualified(location)) { continue; }
            var installed = Path.Combine(location, "Kora.exe");
            if (!string.Equals(Path.GetFullPath(installed), executable, StringComparison.OrdinalIgnoreCase)) { continue; }
            var version = ProductProperty(code, "VersionString");
            if (version is null || !string.Equals(new DirectoryInfo(location).Name, version, StringComparison.Ordinal))
            { throw new InvalidDataException("Installed startup version identity is inconsistent."); }
            var identity = code + "|" + executable + "|" + version + "|"
                + Convert.ToHexString(SHA256.HashData(exe)) + "|" + Convert.ToHexString(SHA256.HashData(dll));
            return new(executable, identity);
        }
        throw new InvalidDataException("Windows Installer product enumeration exceeded the bounded inventory.");
    }

    private static unsafe string? ProductProperty(string product, string property)
    {
        var buffer = new char[32768];
        uint count = (uint)buffer.Length;
        fixed (char* output = buffer)
        {
            // User-unmanaged context only. A machine or another user's installation is not ours to change.
            var result = MsiGetProductInfoEx(product, null, 2, property, output, ref count);
            if (result == 1605) { return null; }
            if (result != 0) { throw new IOException("Windows Installer installation identity could not be read: " + result); }
        }
        return new(buffer, 0, checked((int)count));
    }

    public string? ReadUserCommand() => ReadCommand(RegistryHive.CurrentUser);
    public string? ReadMachineCommand() => ReadCommand(RegistryHive.LocalMachine);
    private static string? ReadCommand(RegistryHive hive)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(RunKey, writable: false);
        var value = key?.GetValue("Kora", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) { return null; }
        if (key!.GetValueKind("Kora") != RegistryValueKind.String || value is not string command)
        { throw new InvalidDataException("Kora startup registration has an unsupported registry type."); }
        return command;
    }

    public byte[] ReadUserApproval()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = root.OpenSubKey(ApprovalKey, writable: false);
        var value = key?.GetValue("Kora", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) { return []; }
        if (key!.GetValueKind("Kora") != RegistryValueKind.Binary || value is not byte[] bytes)
        { throw new InvalidDataException("Kora Windows startup approval has an unsupported registry type."); }
        return bytes;
    }

    public void WriteUserCommand(InstalledLogonApplication installation, string? expected, string? replacement, Func<bool> admitted)
    {
        using var exe = OpenImage(installation.Executable);
        using var dll = OpenImage(Path.ChangeExtension(installation.Executable, ".dll"));
        if (ResolveInstalled(installation.Executable, exe, dll) != installation
            || !string.Equals(ReadUserCommand(), expected, StringComparison.Ordinal) || ReadMachineCommand() is not null
            || !ApprovalPermitsWrite() || !admitted())
        { throw new InvalidOperationException("Installed image, startup ownership, Windows approval or host admission changed before registry commit."); }
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = root.CreateSubKey(RunKey, writable: true);
        if (replacement is null) { key.DeleteValue("Kora", throwOnMissingValue: false); }
        else { key.SetValue("Kora", replacement, RegistryValueKind.String); }
    }

    private bool ApprovalPermitsWrite()
    {
        var bytes = ReadUserApproval();
        return bytes.Length == 0 || bytes.Length == 12 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 2;
    }

    [LibraryImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, char* product);
    [LibraryImport("msi.dll", EntryPoint = "MsiGetProductInfoExW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint MsiGetProductInfoEx(string product, string? userSid, uint context, string property, char* value, ref uint count);
}
