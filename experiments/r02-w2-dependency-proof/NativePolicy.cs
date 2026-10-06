using System.ComponentModel;
using System.Runtime.InteropServices;

namespace W2Proof;

internal static class NativePolicy
{
    internal static uint BlockChildren()
    {
        uint flags = 1;
        if (!SetProcessMitigationPolicy(13, ref flags, 4))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetProcessMitigationPolicy(GetCurrentProcess(), 13, out uint actual, 4))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((actual & 1) == 0) throw new InvalidOperationException("Child policy not applied");
        return actual;
    }

    internal static Observation HandleObservation(long value)
    {
        try
        {
            bool valid = GetHandleInformation((IntPtr)value, out _);
            int error = valid ? 0 : Marshal.GetLastWin32Error();
            return new("inherited.host-handle", error == 6 ? "NotInherited" : "Unknown", error,
                Detail: valid ? "Numeric handle may be reused; not an object-identity proof" : null);
        }
        catch (SEHException error)
        {
            return new("inherited.host-handle", "Unknown", Detail:
                $"Strict-handle probe raised SEHException HRESULT 0x{error.HResult:x8}; not denial evidence");
        }
    }

    internal static Observation LoadNative(string name, string path)
    {
        IntPtr library = LoadLibraryExW(path, IntPtr.Zero, 0x00001100);
        if (library == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return new(name, Admission.ClassifyNative(error), error);
        }
        try
        {
            IntPtr address = GetProcAddress(library, "W2Observe");
            if (address == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(name, "Allowed", Value: Marshal.GetDelegateForFunctionPointer<Observe>(address)());
        }
        finally
        {
            if (!FreeLibrary(library)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Observe();

    internal static void MakeInheritable(IntPtr handle)
    {
        if (!SetHandleInformation(handle, 1, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessMitigationPolicy(int policy, ref uint buffer, nuint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint buffer, nuint length);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr library, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr library);
}
