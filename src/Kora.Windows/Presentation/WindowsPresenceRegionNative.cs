using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kora.Windows.Presentation;

internal sealed partial class WindowsPresenceRegionNative : IWindowsPresenceRegionNative
{
    public void Validate(nint window)
    {
        VerifyWindow(window);
        var probe = CreateRegion(0, 0);
        try
        {
            if (GetWindowRgn(window, probe) != 0)
            {
                throw new InvalidOperationException("The presence shell already has a window region owned by another mechanism.");
            }
        }
        finally
        {
            DeleteRegion(probe);
        }
    }

    public (int Width, int Height) GetSize(nint window)
    {
        VerifyWindow(window);
        if (!GetWindowRect(window, out var bounds)) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        var width = checked(bounds.Right - bounds.Left);
        var height = checked(bounds.Bottom - bounds.Top);
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096)
        {
            throw new InvalidOperationException("The native presence input footprint is outside its physical bounds.");
        }
        return (width, height);
    }

    private static void VerifyWindow(nint window)
    {
        if (window == 0 || GetWindowThreadProcessId(window, out var process) != GetCurrentThreadId()
            || process != Environment.ProcessId || GetAncestor(window, 2) != window)
        {
            throw new InvalidOperationException("The native presence region requires its current-thread host-owned top level.");
        }
        if ((new WindowsPresenceInputNative().GetExtendedStyle(window) & 0x80000) != 0)
        {
            throw new InvalidOperationException("The native presence requires a non-layered Avalonia shell.");
        }
    }

    public nint CreateRegion(int width, int height)
    {
        var region = CreateRectRgn(0, 0, width, height);
        return region != 0 ? region : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void SetRegion(nint window, nint region)
    {
        if (region == 0) { throw new ArgumentException("An owned non-null region is required.", nameof(region)); }
        if (SetWindowRgn(window, region, true) == 0) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    }

    public void DeleteRegion(nint region)
    {
        if (!DeleteObject(region)) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint region);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [LibraryImport("user32.dll")]
    private static partial int GetWindowRgn(nint window, nint region);
}
