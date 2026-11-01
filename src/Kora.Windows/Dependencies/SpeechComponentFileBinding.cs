using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

using Kora.Windows.Coordination;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Dependencies;

internal sealed partial class SpeechComponentFileBinding : IDisposable
{
    private readonly List<SafeFileHandle> directories = [];
    internal SafeFileHandle Last => directories[^1];

    internal void PinAncestors(string path)
    {
        var current = Path.GetPathRoot(path) ?? throw new InvalidDataException("A fixed local speech root is required.");
        PinDirectory(current);
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar))
        {
            if (part.Length == 0) { continue; }
            current = Path.Combine(current, part);
            PinDirectory(current);
        }
    }

    internal void PinDirectory(string path, bool deleting = false)
    {
        var handle = Open(path, directory: true, deleting);
        directories.Add(handle);
    }

    internal static SafeFileHandle Open(string path, bool directory, bool deleting = false)
    {
        var handle = CreateFile(path, (directory ? 0x000200A0u : 0x80020000u) | (deleting ? 0x00010000u : 0),
            directory ? 3u : 1u, nint.Zero, 3, 0x02200000, nint.Zero);
        try
        {
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            { throw Failure("The exact speech-data identity is unavailable."); }
            if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory
                || !directory && info.Links != 1
                || !string.Equals(CoordinationNative.CanonicalPath(handle).TrimEnd('\\'),
                    path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("Speech data is reparse, hard-linked, directory-aliased or replaced."); }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static void RequireIdentity(SafeFileHandle handle, string expected)
    {
        if (!string.Equals(WindowsFileIdentity.Read(handle), expected, StringComparison.Ordinal))
        { throw new InvalidDataException("The exact speech-data object changed. Inspect and review again."); }
    }

    internal static void Delete(SafeFileHandle handle)
    {
        if (!SetFileInformationByHandle(handle, 4, [1], 1))
        { throw Failure("The exact speech-data entry could not be deleted. Inspect the remaining inventory."); }
    }

    internal static void Rename(SafeFileHandle source, SafeFileHandle parent, string name)
    {
        var text = Encoding.Unicode.GetBytes(name);
        var offset = nint.Size == 8 ? 20 : 12;
        var buffer = new byte[offset + text.Length + 2];
        BitConverter.GetBytes((long)parent.DangerousGetHandle()).AsSpan(0, nint.Size)
            .CopyTo(buffer.AsSpan(nint.Size == 8 ? 8 : 4));
        BitConverter.GetBytes(text.Length).CopyTo(buffer, offset - 4);
        text.CopyTo(buffer, offset);
        if (NtSetInformationFile(source, out _, buffer, (uint)buffer.Length, 65) != 0)
        { throw new IOException("Atomic no-replace speech-data retirement/publication failed. Inspect before retrying."); }
    }

    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastWin32Error()));

    public void Dispose()
    {
        for (var index = directories.Count - 1; index >= 0; index--) { directories[index].Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, ModifiedLow, ModifiedHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { internal nint Status, Information; }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint sharing, nint security,
        uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle handle, int kind, byte[] data, uint size);
    [LibraryImport("ntdll.dll")]
    private static partial uint NtSetInformationFile(SafeFileHandle handle, out IoStatus status, byte[] data, uint size, int kind);
}