using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Coordination;

internal static partial class CoordinationNative
{
    internal const uint Synchronize = 0x00100000;
    internal const uint QueryLimitedInformation = 0x1000;

    internal static Mutex CreateUserMutex(string name, string sid)
    {
        const uint rights = Synchronize | 0x00020000 | 1;
        var sddl = $"D:P(A;;0x{rights:x};;;{sid})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
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
            var handle = CreateMutexEx(ref attributes, name, 0, rights);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                VerifyMutexPermissions(handle, sddl);
                return new Mutex { SafeWaitHandle = handle };
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    private static void VerifyMutexPermissions(SafeWaitHandle handle, string expected)
    {
        var error = GetSecurityInfo(handle, 6, 4, out _, out _, out _, out _, out var descriptor);
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }

        try
        {
            if (!ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, 1, 4, out var text, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!string.Equals(Marshal.PtrToStringUni(text), expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The coordinator object is not restricted to this user.");
                }
            }
            finally
            {
                _ = LocalFree(text);
            }
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    internal static FileStream CreateUserContinuityFile(string path, string sid)
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
            var handle = CreateFile(path, 0x40000000, 0, ref attributes, 1, 0x80, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new FileStream(handle, FileAccess.Write);
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    internal static (string Sid, int SessionId) ReadProcessToken(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, 8, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (token)
        {
            _ = GetTokenInformation(token, 1, IntPtr.Zero, 0, out var length);
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 1, buffer, length, out _) ||
                    !GetTokenInformation(token, 12, out var session, sizeof(int), out _) ||
                    !GetTokenInformation(token, 20, out var elevated, sizeof(int), out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (elevated != 0)
                {
                    throw new InvalidOperationException("Kora instance ownership requires an unprivileged process.");
                }

                var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
                return (sid.Value, session);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    internal static string ReadImagePath(SafeProcessHandle process)
    {
        var buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            var size = 32768;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return Marshal.PtrToStringUni(buffer, size)!;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static string CanonicalPath(SafeFileHandle file)
    {
        var buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            var size = GetFinalPathNameByHandle(file, buffer, 32768, 0);
            if (size is 0 or >= 32768)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var path = Marshal.PtrToStringUni(buffer, (int)size)!;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Remote executable locations cannot be coordinated.");
            }

            return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        internal int InheritHandle;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSecurityDescriptorToStringSecurityDescriptor(IntPtr descriptor, uint revision, uint information, out IntPtr text, out uint size);

    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityInfo(SafeWaitHandle handle, int objectType, uint information, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateMutexExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeWaitHandle CreateMutexEx(ref SecurityAttributes attributes, string name, uint flags, uint access);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, ref SecurityAttributes attributes, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, IntPtr path, ref int length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, IntPtr path, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll")]
    internal static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returnLength);
}
