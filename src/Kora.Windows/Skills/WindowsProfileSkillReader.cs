using System.ComponentModel;
using System.Runtime.InteropServices;

using Kora.Core.Skills;
using Kora.Windows.Coordination;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

public sealed partial class WindowsProfileSkillReader : ISharedSkillSourceReader
{
    private readonly string profile;
    private readonly Action? afterInventory;

    public WindowsProfileSkillReader() : this(ReadProfileKnownFolder()) { }

    internal WindowsProfileSkillReader(string profile, Action? afterInventory = null)
    {
        this.profile = ValidateAbsolutePath(profile);
        this.afterInventory = afterInventory;
    }

    public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = ValidateAbsolutePath(selectedRoot);
            if (!root.StartsWith(profile + '\\', StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("The selected source must be below the Windows profile known folder."); }
            var relative = SharedSkillSource.ValidateRelativePath(root[(profile.Length + 1)..]);
            using var pinned = PinAncestors(root);
            cancellationToken.ThrowIfCancellationRequested();
            return new SharedSkillSource(Guid.NewGuid(), relative, WindowsFileIdentity.Read(pinned.Last));
        }, cancellationToken));

    public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
        new(Task.Run(async () =>
        {
            ArgumentNullException.ThrowIfNull(source);
            cancellationToken.ThrowIfCancellationRequested();
            var root = Path.Combine(profile, source.ProfileRelativeRoot);
            using var pinned = PinAncestors(root);
            if (!string.Equals(WindowsFileIdentity.Read(pinned.Last), source.DirectoryIdentity, StringComparison.Ordinal))
            { throw new SharedSkillUnavailableException("registered-source-replaced"); }
            var entries = new List<string>();
            Inventory(root, 0, pinned, entries, cancellationToken);
            afterInventory?.Invoke();
            var files = entries.Where(path => string.Equals(Path.GetFileName(path), "SKILL.md",
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length > SharedSkillCatalogue.MaximumPackages)
            { throw new SharedSkillUnavailableException("package-count-limit"); }
            var result = new List<SharedSkillSnapshot>();
            var total = 0;
            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var handle = OpenPinned(file, directory: false, content: true);
                await using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: true);
                if (stream.Length is 0 or > SharedSkillSnapshot.MaximumBytes
                    || stream.Length > SharedSkillCatalogue.MaximumTotalBytes - total)
                { throw new SharedSkillUnavailableException("file-or-total-byte-limit"); }
                var bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (stream.Position != stream.Length)
                { throw new SharedSkillUnavailableException("file-changed-during-read"); }
                total += bytes.Length;
                var directory = Path.GetDirectoryName(file)!;
                var otherFiles = entries.Where(entry => !string.Equals(entry, file, StringComparison.Ordinal)
                    && entry.StartsWith(directory + '\\', StringComparison.OrdinalIgnoreCase))
                    .Select(entry => Path.GetRelativePath(root, entry)).Order(StringComparer.Ordinal).ToArray();
                var snapshot = SharedSkillSnapshot.Parse(source.Id, Path.GetRelativePath(root, file), bytes, otherFiles.Length > 0, otherFiles);
                if (!string.Equals(Path.GetFileName(file), "SKILL.md", StringComparison.Ordinal))
                { snapshot = snapshot.WithUnavailableReason("noncanonical-skill-file-name"); }
                result.Add(snapshot);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new SharedSkillCatalogue(source, result);
        }, cancellationToken));

    private static void Inventory(string directory, int depth, PinnedHandles pinned, List<string> entries,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= SharedSkillCatalogue.MaximumEntries)
            { throw new SharedSkillUnavailableException("entry-count-limit"); }
            var path = ValidateAbsolutePath(entry);
            var isDirectory = File.GetAttributes(path).HasFlag(FileAttributes.Directory);
            pinned.Add(OpenPinned(path, isDirectory, content: false));
            entries.Add(path);
            if (isDirectory)
            {
                if (depth >= SharedSkillCatalogue.MaximumDepth)
                { throw new SharedSkillUnavailableException("directory-depth-limit"); }
                Inventory(path, depth + 1, pinned, entries, cancellationToken);
            }
        }
    }

    private static PinnedHandles PinAncestors(string path)
    {
        var pinned = new PinnedHandles();
        try
        {
            var root = Path.GetPathRoot(path)!;
            pinned.Add(OpenPinned(root, directory: true, content: false));
            var current = root;
            foreach (var part in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                pinned.Add(OpenPinned(current, directory: true, content: false));
            }
            return pinned;
        }
        catch { pinned.Dispose(); throw; }
    }

    private static SafeFileHandle OpenPinned(string path, bool directory, bool content)
    {
        // Metadata-only file handles do not participate in Windows data-share checks.
        // Open read access even for inventory (without reading contents) to exclude writers.
        var handle = CreateFile(path, 0x80000000U, 1, IntPtr.Zero, 3,
            0x00200000U | (directory ? 0x02000000U : 0U) | (content ? 0x40000000U : 0U), IntPtr.Zero);
        try
        {
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                throw new SharedSkillUnavailableException(error is 2 or 3 ? "source-missing" : "source-unreadable-or-busy");
            }
            if (!GetFileInformationByHandle(handle, out var info))
            { throw new SharedSkillUnavailableException("source-identity-unavailable"); }
            if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory
                || !directory && info.Links != 1
                || !string.Equals(CoordinationNative.CanonicalPath(handle).TrimEnd('\\'),
                    path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            { throw new SharedSkillUnavailableException("link-reparse-alias-or-entry-type"); }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static string ValidateAbsolutePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Length > 1024 || path.Length < 3 || !char.IsAsciiLetter(path[0])
            || path[1] != ':' || path[2] != '\\' || path.Contains('/', StringComparison.Ordinal)
            || path[2..].Split('\\').Any(part => part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|'))
            || !string.Equals(Path.GetFullPath(path).TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Select an exact local drive path, without device, network or traversal aliases."); }
        if (GetDriveType(Path.GetPathRoot(path)!) != 3)
        { throw new SharedSkillUnavailableException("non-fixed-or-remote-volume"); }
        return path.TrimEnd('\\');
    }

    private static string ReadProfileKnownFolder()
    {
        var id = new Guid("5e6c858f-0e22-4760-9afe-ea3317b67173");
        var result = SHGetKnownFolderPath(in id, 0x00004000, IntPtr.Zero, out var value);
        if (result < 0) { throw new Win32Exception(result, "The Windows profile known folder is unavailable."); }
        try { return Marshal.PtrToStringUni(value) ?? throw new InvalidDataException("The Windows profile known folder is unknown."); }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    private sealed class PinnedHandles : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        internal SafeFileHandle Last => handles[^1];
        internal void Add(SafeFileHandle handle) => handles.Add(handle);
        public void Dispose() { foreach (var handle in handles) { handle.Dispose(); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal uint CreatedLow;
        internal uint CreatedHigh;
        internal uint AccessedLow;
        internal uint AccessedHigh;
        internal uint ModifiedLow;
        internal uint ModifiedHigh;
        internal uint Volume;
        internal uint SizeHigh;
        internal uint SizeLow;
        internal uint Links;
        internal uint IndexHigh;
        internal uint IndexLow;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folder, uint flags, IntPtr token, out IntPtr path);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);
}
