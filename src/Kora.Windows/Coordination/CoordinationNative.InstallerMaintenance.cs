using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Coordination;

internal static partial class CoordinationNative
{
    internal static FileStream CreateInstallerContinuityFile(string path, string sid)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                $"D:P(A;;FA;;;{sid})", 1, out var descriptor, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
            };
            // Read/write/delete access, read sharing only, CREATE_NEW, write-through. No DeleteOnClose.
            var handle = CreateInstallerFile(path, 0xC0010000, 1, ref attributes, 1, 0x80000080, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try { return new FileStream(handle, FileAccess.ReadWrite); }
            catch { handle.Dispose(); throw; }
        }
        finally { _ = LocalFree(descriptor); }
    }

    internal static void DeleteInstallerContinuityFile(SafeFileHandle handle)
    {
        var disposition = new InstallerFileDisposition { DeleteFile = 1 };
        if (!SetInstallerFileInformation(handle, 4, ref disposition, 1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstallerFileDisposition
    {
        internal byte DeleteFile;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInstallerFileInformation(SafeFileHandle handle, int informationClass,
        ref InstallerFileDisposition information, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateInstallerFile(string path, uint access, uint share,
        ref SecurityAttributes attributes, uint disposition, uint flags, IntPtr template);
}
