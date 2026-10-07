using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Storage;

internal static partial class WindowsFileIdentity
{
    internal static string Read(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 18, out var identity, Marshal.SizeOf<FileIdentity>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The private storage file identity is unavailable.");
        }
        return $"{identity.Volume:x16}{identity.Low:x16}{identity.High:x16}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdentity
    {
        internal ulong Volume;
        internal ulong Low;
        internal ulong High;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileIdentity information, int size);
}
