using System.ComponentModel;
using System.Runtime.InteropServices;

using Kora.Core.Voice;

namespace Kora.Windows.Input;

internal sealed partial class WindowsGlobalPttNative : IGlobalPttNative
{
    private const int HotkeyId = 1;
    private const uint HotkeyMessage = 0x0312;
    private const uint WakeMessage = 0x8001;
    private const uint RemoveMessage = 1;
    private const uint AllInput = 0x04FF;
    private const uint InputAvailable = 4;
    private const uint WaitTimeout = 258;

    public uint Initialize()
    {
        _ = PeekMessage(out _, 0, 0, 0, 0);
        return GetCurrentThreadId();
    }

    public void Wake(uint threadId)
    {
        if (!PostThreadMessage(threadId, WakeMessage, 0, 0)) { ThrowNative("Waking global PTT owner"); }
    }

    public void Register(GlobalPttShortcut shortcut)
    {
        // MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT. No keyboard hook or input forwarding.
        if (!RegisterHotKey(0, HotkeyId, 0x0002 | 0x0004 | 0x4000, Key(shortcut)))
        { ThrowNative("Global PTT chord could not be registered; it may be owned by another application. No fallback"); }
    }

    public void Unregister()
    {
        if (!UnregisterHotKey(0, HotkeyId)) { ThrowNative("Removing global PTT registration"); }
        while (PeekMessage(out _, 0, HotkeyMessage, HotkeyMessage, RemoveMessage)) { }
    }

    public GlobalPttNativeMessage Wait(bool held)
    {
        if (held)
        {
            var result = MsgWaitForMultipleObjectsEx(0, 0, 20, AllInput, InputAvailable);
            if (result == uint.MaxValue) { ThrowNative("Waiting for global PTT release"); }
            if (result == WaitTimeout) { return GlobalPttNativeMessage.CheckRelease; }
        }
        var read = GetMessage(out var message, 0, 0, 0);
        if (read == -1) { ThrowNative("Reading global PTT message"); }
        if (read == 0) { throw new InvalidOperationException("Global PTT message owner was unexpectedly terminated."); }
        return message.Message == HotkeyMessage && message.WParam == HotkeyId
            ? GlobalPttNativeMessage.Press : GlobalPttNativeMessage.Wake;
    }

    public bool IsHeld(GlobalPttShortcut shortcut) =>
        Pressed(Key(shortcut)) && Pressed(0x11) && Pressed(0x10);

    private static uint Key(GlobalPttShortcut shortcut) => shortcut == GlobalPttShortcut.ControlShiftSpace ? 0x20u
        : shortcut == GlobalPttShortcut.ControlShiftF9 ? 0x78u
        : throw new InvalidOperationException("An unassigned global PTT chord cannot be registered.");
    private static bool Pressed(uint key) => (GetAsyncKeyState(unchecked((int)key)) & 0x8000) != 0;
    private static void ThrowNative(string operation) => throw new Win32Exception(Marshal.GetLastPInvokeError(), operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint Window;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out NativeMessage message, nint window, uint min, uint max, uint remove);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessage(out NativeMessage message, nint window, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint handles, uint milliseconds, uint mask, uint flags);
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);
}
