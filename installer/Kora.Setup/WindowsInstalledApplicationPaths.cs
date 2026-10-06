using System.Runtime.InteropServices;

namespace Kora.Setup;

internal static class WindowsInstalledApplicationPaths
{
    private static readonly Guid UserPrograms = new("5CD7AEE2-2219-4A67-B85D-6C9CE15660CB");
    private static readonly Guid MachinePrograms = new("6D809377-6AF0-444B-8957-A3773F02200E");

    public static string Executable(InstallScope scope, string productVersion)
    {
        if (!Enum.IsDefined(scope)) { throw new ArgumentOutOfRangeException(nameof(scope)); }
        if (!Version.TryParse(productVersion, out var version) || version.Revision != -1 || version.Build < 0)
        {
            throw new InvalidDataException("Installed application resolution requires the MSI's numeric product version.");
        }
        var folder = scope == InstallScope.CurrentUser ? UserPrograms : MachinePrograms;
        // Resolve future install locations without verifying or creating directories.
        var result = SHGetKnownFolderPath(in folder, 0x4000, nint.Zero, out var pointer);
        try
        {
            Marshal.ThrowExceptionForHR(result);
            var programs = Marshal.PtrToStringUni(pointer) ?? throw new InvalidDataException("Windows Programs folder is unknown.");
            return Path.Combine(programs, "Kora", productVersion, "Kora.exe");
        }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid folder, uint flags, nint token, out nint path);
}
