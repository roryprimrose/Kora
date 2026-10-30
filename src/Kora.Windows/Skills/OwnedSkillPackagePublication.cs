using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Kora.Windows.Coordination;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

/// <summary>Pins every ancestor and commits a whole directory relative to its pinned destination parent.</summary>
internal sealed partial class OwnedSkillPackagePublication : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];
    internal SafeFileHandle Last => handles[^1];

    internal void PinAncestors(string path)
    {
        var current = Path.GetPathRoot(path) ?? throw new InvalidDataException("A fixed local package root is required.");
        PinDirectory(current);
        foreach (var component in path[current.Length..].Split(Path.DirectorySeparatorChar))
        {
            if (component.Length == 0) { continue; }
            current = Path.Combine(current, component);
            PinDirectory(current);
        }
    }

    internal SafeFileHandle PinDirectory(string path, bool forRename = false)
    {
        var handle = CreateFile(path, forRename ? 0x000300A0u : 0x000200A0u,
            3, nint.Zero, 3, 0x02200000, nint.Zero);
        try
        {
            Verify(handle, path, directory: true);
            handles.Add(handle);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static SafeFileHandle OpenFile(string path)
        => OpenFileCore(path, delete: false);

    internal void PinSourceFile(string path)
    {
        var handle = CreateFile(path, 0x80020000u, 1, nint.Zero, 3, 0x00200000, nint.Zero);
        try { Verify(handle, path, directory: false); handles.Add(handle); }
        catch { handle.Dispose(); throw; }
    }

    private static SafeFileHandle OpenFileCore(string path, bool delete)
    {
        // Share rename, not writes: the enclosing directory is renamed while both exact file objects stay pinned.
        var handle = CreateFile(path, delete ? 0x80030000u : 0x80020000u, 5, nint.Zero, 3, 0x00200000, nint.Zero);
        try { Verify(handle, path, directory: false); return handle; }
        catch { handle.Dispose(); throw; }
    }

    internal static void DeleteExactFile(string path, string identity)
    {
        using var handle = OpenFileCore(path, delete: true);
        if (!string.Equals(WindowsFileIdentity.Read(handle), identity, StringComparison.Ordinal))
        { throw new InvalidDataException("Private staging file identity changed; external data was not deleted."); }
        MarkDeleted(handle);
    }

    internal static void MarkDeleted(SafeFileHandle handle)
    {
        if (!SetFileInformationByHandle(handle, 4, [1], 1))
        { throw Failure("The exact private staging entry could not be retired."); }
    }

    internal static void Verify(SafeFileHandle handle, string path, bool directory)
    {
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
        { throw Failure("The pinned skill package identity is unavailable."); }
        if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory
            || !directory && info.Links != 1
            || !string.Equals(CoordinationNative.CanonicalPath(handle).TrimEnd('\\'),
                path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("A skill package path is reparse, hard-linked, aliased or replaced."); }
    }

    internal static void Commit(SafeFileHandle staging, SafeFileHandle parent, string version)
    {
        var name = Encoding.Unicode.GetBytes(version);
        var offset = nint.Size == 8 ? 20 : 12;
        var buffer = new byte[offset + name.Length + 2];
        // FILE_RENAME_INFORMATION_EX with no replace flags, relative to the pinned exact ID directory.
        BitConverter.GetBytes((long)parent.DangerousGetHandle()).AsSpan(0, nint.Size)
            .CopyTo(buffer.AsSpan(nint.Size == 8 ? 8 : 4));
        BitConverter.GetBytes(name.Length).CopyTo(buffer, offset - 4);
        name.CopyTo(buffer, offset);
        var status = NtSetInformationFile(staging, out _, buffer, (uint)buffer.Length, 65);
        if (status == 0xc0000035)
        { throw new InvalidOperationException("That exact skill version already exists. Choose a new ID/version or cancel; nothing was replaced."); }
        if (status != 0) { throw new IOException("Atomic no-replace skill directory publication failed.", new Win32Exception(unchecked((int)status))); }
    }

    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastWin32Error()));
    public void Dispose()
    {
        for (var index = handles.Count - 1; index >= 0; index--) { handles[index].Dispose(); }
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
