using System.Runtime.InteropServices;

using Kora.Core.Platform;
using Kora.Windows.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Session;

public sealed partial class WindowsSessionController(
    ILogger<WindowsSessionController> logger) : ISessionController
{
    private const int CurrentSession = -1;
    private const int SessionStateUnlocked = 1;
    private const uint DesktopSwitchDesktop = 0x0100;

    public bool IsCurrentSessionUnlocked()
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                CurrentSession,
                WtsInfoClass.SessionInfoEx,
                out var buffer,
                out var bytesReturned))
        {
            WindowsLog.Warning(logger, "Windows session state could not be determined");
            return false;
        }

        try
        {
            if (bytesReturned < Marshal.SizeOf<WtsInfoEx>())
            {
                WindowsLog.Warning(logger, "Windows returned incomplete session state");
                return false;
            }

            var sessionInfo = Marshal.PtrToStructure<WtsInfoEx>(buffer);
            if (sessionInfo.Level != 1)
            {
                WindowsLog.Warning(logger, "Windows returned an unsupported session state level");
                return false;
            }

            return IsSessionUnlocked(
                sessionInfo.Data.Level1.ConnectionState == WtsConnectState.Active,
                sessionInfo.Data.Level1.SessionFlags == SessionStateUnlocked,
                CanSwitchToInputDesktop());
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    internal static bool IsSessionUnlocked(
        bool isActive,
        bool sessionFlagUnlocked,
        bool inputDesktopAvailable) =>
        isActive && (sessionFlagUnlocked || inputDesktopAvailable);

    private static bool CanSwitchToInputDesktop()
    {
        var desktop = OpenInputDesktop(
            flags: 0,
            inherit: false,
            DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return SwitchDesktop(desktop);
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }

    public bool LockCurrentSession()
    {
        WindowsLog.Information(logger, "Windows session lock was requested");
        var result = LockWorkStation();
        if (!result)
        {
            WindowsLog.Warning(logger, "Windows did not accept the session lock request");
        }

        return result;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(
        IntPtr serverHandle,
        int sessionId,
        WtsInfoClass infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(IntPtr memory);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint desiredAccess);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SwitchDesktop(IntPtr desktop);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(IntPtr desktop);

    private enum WtsInfoClass
    {
        SessionInfoEx = 25,
    }

    private enum WtsConnectState
    {
        Active = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsInfoEx
    {
        public int Level;

        public WtsInfoExLevel Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct WtsInfoExLevel
    {
        [FieldOffset(0)]
        public WtsInfoExLevel1 Level1;

        [FieldOffset(0)]
        private readonly long alignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsInfoExLevel1
    {
        public int SessionId;

        public WtsConnectState ConnectionState;

        public int SessionFlags;
    }
}