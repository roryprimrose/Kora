using System.ComponentModel;
using System.Runtime.InteropServices;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceStore
{
    private string Partition => Path.Combine(paths.LocalRoot, "KnowledgeSourcesV1");

    private void VerifyStorageInventory()
    {
        var directories = Directory.EnumerateDirectories(Partition).Take(2).ToArray();
        if (directories.Length != 1 || !string.Equals(directories[0], Path.Combine(Partition, "Artifacts"), StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Uninventoried private storage directories hold source admission and cleanup."); }
    }

    private StorageIdentity? HoldStorageIdentity(bool created)
    {
        if (created) { return null; }
        try { return new([Path.Combine(Partition, "sources.db"), Path.Combine(Partition, "sources.db-journal")]); }
        catch (FileNotFoundException exception)
        { throw new InvalidDataException("An existing private source database or journal is missing; replacement is forbidden.", exception); }
    }

    private sealed class StorageIdentity : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        internal StorageIdentity(IEnumerable<string> files)
        {
            try
            {
                foreach (var file in files)
                {
                    RestrictedStorageDirectory.RejectReparseAncestors(file);
                    handles.Add(File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                }
                Validate();
            }
            catch { Dispose(); throw; }
        }
        internal void Validate()
        {
            foreach (var handle in handles)
            {
                if (!GetFileInformationByHandleEx(handle, 1, out var info, Marshal.SizeOf<StandardInfo>()))
                { throw new IOException("The owned source storage identity is unavailable.", new Win32Exception(Marshal.GetLastWin32Error())); }
                if (info.Links != 1 || info.DeletePending != 0 || info.Directory != 0)
                { throw new InvalidDataException("Linked, deleting or non-file storage cannot authorize source use or deletion."); }
            }
        }
        public void Dispose() { foreach (var handle in handles) { handle.Dispose(); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        internal long AllocationSize;
        internal long EndOfFile;
        internal uint Links;
        internal byte DeletePending;
        internal byte Directory;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out StandardInfo information, int size);
}
