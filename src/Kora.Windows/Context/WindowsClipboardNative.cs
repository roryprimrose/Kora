using System.Runtime.InteropServices;

namespace Kora.Windows.Context;

internal sealed partial class WindowsClipboardNative : IClipboardNative
{
    public int LastError => Marshal.GetLastPInvokeError();
    public uint GetSequence() => GetClipboardSequenceNumber();
    public bool Open() => OpenClipboard(nint.Zero);
    public bool Close() => CloseClipboard();
    public bool HasUnicodeText() => IsClipboardFormatAvailable(13);
    public nint GetUnicodeText() => GetClipboardData(13);
    public nuint GetSize(nint handle) => GlobalSize(handle);
    public nint Lock(nint handle) => GlobalLock(handle);
    public bool Unlock(nint handle)
    {
        Marshal.SetLastPInvokeError(0);
        return GlobalUnlock(handle) || Marshal.GetLastPInvokeError() == 0;
    }
    public char ReadCodeUnit(nint pointer, int index) => (char)Marshal.ReadInt16(pointer, index * sizeof(char));

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint format);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint GlobalSize(nint handle);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint handle);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint handle);
}
