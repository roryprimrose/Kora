using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kora.Windows.Presentation;

public static partial class WindowsPresenceMotionPreference
{
    public static bool IsAnimationEnabled()
    {
        var success = SystemParametersInfo(0x1042, 0, out var enabled, 0);
        return Resolve(success, enabled, Marshal.GetLastPInvokeError());
    }

    internal static bool Resolve(bool success, bool enabled, int error) =>
        success ? enabled : throw new Win32Exception(error, "Cannot read the Windows animation preference.");

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
