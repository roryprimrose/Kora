using System.ComponentModel;
using System.Runtime.InteropServices;
using Kora.Core.Skills;
using Kora.Windows.Dependencies;

namespace Kora.Windows.Skills.RuntimeSetup;

internal sealed partial class DevelopmentBundledWorkerProfile(DevelopmentPreviewAdmission admission)
{
    private BundledRuntimeInstallation? installation;
    private BundledWorkerProfile? profile;
    private DevelopmentPreviewLease? lease;

    internal BundledWorkerProfile? Resolve(BundledRuntimeInspection state)
    {
        var admitted = admission.Resolve(DevelopmentPreviewCapability.BundledWorker);
        if (admitted is null
            || state.Preparation != BundledRuntimePreparation.Prepared || state.Installation is not { } current)
        { installation = null; profile = null; lease = null; return null; }
        if (!ReferenceEquals(installation, current) || !ReferenceEquals(lease, admitted))
        {
            if (DeriveAppContainerSidFromAppContainerName("Kora.Development.FixedWorker." + Guid.NewGuid().ToString("N"), out var sid) != 0)
            { throw new Win32Exception("The development worker AppContainer identity could not be derived."); }
            try
            {
                if (!ConvertSidToStringSid(sid, out var text))
                { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                try
                {
                    var identity = Marshal.PtrToStringUni(text)
                        ?? throw new InvalidDataException("The worker AppContainer identity is unavailable.");
                    var executable = current.Files.Single(file => string.Equals(file.Name, "pwsh.exe", StringComparison.OrdinalIgnoreCase));
                    profile = new(current.Executable, executable.Digest, identity, current.InventoryDigest);
                    installation = current;
                    lease = admitted;
                }
                finally { LocalFree(text); }
            }
            finally { FreeSid(sid); }
        }
        return profile;
    }

    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DeriveAppContainerSidFromAppContainerName(string name, out nint sid);
    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSidToStringSid(nint sid, out nint text);
    [LibraryImport("advapi32.dll")]
    private static partial nint FreeSid(nint sid);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
