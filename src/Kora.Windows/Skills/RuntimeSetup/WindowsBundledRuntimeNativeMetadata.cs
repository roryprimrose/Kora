using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

using Microsoft.Win32;

namespace Kora.Windows.Skills.RuntimeSetup;

internal sealed class WindowsBundledRuntimeNativeMetadata : IBundledRuntimeNativeMetadata
{
    public void RequireSupportedPlatform()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
        { throw new PlatformNotSupportedException("The protected installed PowerShell candidate requires Windows x64, build 19041 or later; containment remains unqualified."); }
    }

    public string InspectProtection(string path, bool directory, bool ancestorOnly)
    {
        FileSystemSecurity security = directory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        WindowsBundledRuntimeInstallationInspector.VerifyProtected(security, ancestorOnly);
        return security.GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner);
    }

    public string InspectExecutable(string path, FileStream input)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        if (version.FileMajorPart != 7 || version.FileMinorPart < 4)
        { throw new InvalidDataException("The protected installed candidate is not the supported PowerShell 7.4-or-later family. No alternative path was searched."); }
        input.Position = 0;
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
        { throw new InvalidDataException("The exact PowerShell executable is not x64."); }
        WindowsBundledRuntimeSignature.VerifyMicrosoft(path);
        return version.FileVersion ?? throw new InvalidDataException("The installed PowerShell file version is unavailable.");
    }

    public string DeviceDigest()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
        var machineId = machine?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(machineId) || identity.User is null)
        { throw new InvalidDataException("The current runtime device/profile identity is unavailable."); }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            machineId + "\n" + identity.User.Value + "\n" + Environment.OSVersion.VersionString)));
    }
}
