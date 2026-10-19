using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Presence.Performance;

internal sealed partial record ResourceSnapshot(double WorkingSetMiB, double PrivateMiB, int Handles, uint GdiObjects, uint UserObjects)
{
    public static ResourceSnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        return new ResourceSnapshot(process.WorkingSet64 / 1048576d, process.PrivateMemorySize64 / 1048576d,
            process.HandleCount, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1));
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(nint process, uint flags);
}
