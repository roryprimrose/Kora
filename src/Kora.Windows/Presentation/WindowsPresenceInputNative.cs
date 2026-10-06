using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kora.Windows.Presentation;

internal sealed partial class WindowsPresenceInputNative : IWindowsPresenceInputNative
{
    private const int ExtendedStyleIndex = -20;

    public bool IsControlPressed() => IsPressed(0x11);

    public bool IsMouseButtonPressed() =>
        IsPressed(0x01) || IsPressed(0x02) || IsPressed(0x04) || IsPressed(0x05) || IsPressed(0x06);

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public uint GetExtendedStyle(nint window)
    {
        var result = GetWindowLong(window, ExtendedStyleIndex);
        if (result == 0 && Marshal.GetLastPInvokeError() is var error && error != 0)
        {
            throw new Win32Exception(error);
        }
        return unchecked((uint)result);
    }

    public void SetExtendedStyle(nint window, uint style)
    {
        var result = SetWindowLong(window, ExtendedStyleIndex, unchecked((int)style));
        if (result == 0 && Marshal.GetLastPInvokeError() is var error && error != 0)
        {
            throw new Win32Exception(error);
        }
    }

    public void EnableLayeredRendering(nint window)
    {
        if (!SetLayeredWindowAttributes(window, 0, byte.MaxValue, 0x02))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void ShowMoveCursorIfOverWindow(nint window)
    {
        if (!GetCursorPos(out var position))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (WindowFromPoint(position) != window)
        {
            return;
        }

        // Ctrl can change while the pointer is stationary, before Avalonia receives a pointer move.
        _ = SetCursor(LoadMoveCursor());
    }

    internal static nint LoadMoveCursor()
    {
        var cursor = LoadCursor(nint.Zero, 32646); // IDC_SIZEALL is a shared Windows cursor.
        return cursor != nint.Zero
            ? cursor
            : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong(nint window, int index, int value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint position);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(NativePoint position);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);
}
