using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ContainmentProof;

internal static class NativeProcessHandle
{
    internal static IntPtr Duplicate(IntPtr handle)
    {
        var current = GetCurrentProcess();
        if (!DuplicateHandle(current, handle, current, out var copy, 0, false, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return copy;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess,
        out IntPtr target, uint access, bool inherit, uint options);
}
