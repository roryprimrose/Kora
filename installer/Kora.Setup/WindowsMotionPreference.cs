using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kora.Setup;

internal static class WindowsMotionPreference
{
    public static bool IsAnimationEnabled()
    {
        if (!SystemParametersInfo(0x1042, 0, out var enabled, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot read the Windows animation preference.");
        }

        return enabled;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
