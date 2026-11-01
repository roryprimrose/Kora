using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;

using Kora.Core.Dependencies;
using Kora.Windows.Storage;

namespace Kora.Windows.Dependencies;

public sealed class WindowsSpeechComponentFiles(IApplicationDataPaths paths) : ISpeechComponentFiles
{
    private readonly RestrictedStorageDirectory directory = new(paths, includeKeys: false, partitionName: "SpeechComponentsV1");
    private readonly Dictionary<string, string> createdFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock creation = new();

    public string Root => directory.Artifacts;

    public bool RootExists()
    {
        if (!EntryExists(paths.LocalRoot)) { return false; }
        if (!directory.HasExistingPartition()) { return false; }
        directory.Verify();
        return true;
    }

    public void EnsureRoot()
    {
        RequireInteractiveUser();
        var ancestor = paths.LocalRoot;
        while (!EntryExists(ancestor))
        {
            ancestor = Path.GetDirectoryName(ancestor)
                ?? throw new UnauthorizedAccessException("The application data root has no existing local ancestor.");
        }
        RestrictedStorageDirectory.RejectReparseAncestors(ancestor);
        Directory.CreateDirectory(paths.LocalRoot);
        if (directory.HasExistingPartition()) { directory.Verify(); }
        else { directory.CreateNew(); }
    }

    public async Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        RequireInteractiveUser();
        return await Task.Run(() => directory.AcquireBoundedLease(requireExisting: false, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public bool Exists(string path)
    {
        ValidateContained(path);
        if (!EntryExists(path)) { return false; }
        VerifyEntry(path);
        return true;
    }

    public IReadOnlyList<string> Names(string directoryPath)
    {
        VerifyEntry(directoryPath);
        return Directory.GetFileSystemEntries(directoryPath).Select(path =>
        {
            VerifyEntry(path);
            return Path.GetFileName(path);
        }).ToArray();
    }

    public Stream Read(string path)
    {
        using var pinned = PinDirectory(Path.GetDirectoryName(path)!);
        var handle = SpeechComponentFileBinding.Open(path, directory: false);
        try
        {
            VerifyEntry(path);
            return new FileStream(handle, FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    public string Identity(string path)
    {
        var isDirectory = File.GetAttributes(path).HasFlag(FileAttributes.Directory);
        using var pinned = PinDirectory(isDirectory ? path : Path.GetDirectoryName(path)!);
        using var handle = SpeechComponentFileBinding.Open(path, isDirectory);
        VerifyEntry(path);
        return WindowsFileIdentity.Read(handle);
    }

    public IDisposable PinDirectory(string path)
    {
        ValidateContained(path);
        var pinned = new SpeechComponentFileBinding();
        try
        {
            pinned.PinAncestors(path);
            VerifyEntry(path);
            return pinned;
        }
        catch { pinned.Dispose(); throw; }
    }

    public async Task DeleteVerifiedFileAsync(string path, string identity, long bytes, string digest,
        Func<bool> admitted, CancellationToken cancellationToken)
    {
        using var pinned = PinDirectory(Path.GetDirectoryName(path)!);
        using var handle = SpeechComponentFileBinding.Open(path, directory: false, deleting: true);
        VerifyEntry(path);
        SpeechComponentFileBinding.RequireIdentity(handle, identity);
        await using var stream = new FileStream(handle, FileAccess.Read);
        if (stream.Length != bytes || !string.Equals(
            Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)), digest, StringComparison.Ordinal))
        { throw new InvalidDataException("Speech-data bytes changed after review. No replacement was deleted."); }
        cancellationToken.ThrowIfCancellationRequested();
        VerifyEntry(path);
        if (!admitted()) { throw new UnauthorizedAccessException("Speech-data removal authority changed."); }
        SpeechComponentFileBinding.Delete(handle);
    }

    public Stream Create(string path)
    {
        ValidateContained(path);
        VerifyEntry(Path.GetDirectoryName(path)!);
        var stream = directory.CreateNewFile(path, FileOptions.Asynchronous);
        try
        {
            lock (creation) { createdFiles[path] = WindowsFileIdentity.Read(stream.SafeFileHandle); }
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public void CreateDirectory(string path)
    {
        ValidateContained(path);
        VerifyEntry(Path.GetDirectoryName(path)!);
        if (EntryExists(path)) { throw new IOException("The component staging destination already exists."); }
        using var identity = WindowsIdentity.GetCurrent();
        RestrictedStorageDirectory.CreateRestrictedDirectory(path,
            identity.User ?? throw new UnauthorizedAccessException("The interactive profile is unknown."));
        VerifyEntry(path);
    }

    public void MoveDirectory(string source, string destination, string? expectedIdentity = null)
    {
        ValidateContained(destination);
        using var pinned = new SpeechComponentFileBinding();
        pinned.PinAncestors(Path.GetDirectoryName(destination)!);
        using var handle = SpeechComponentFileBinding.Open(source, directory: true, deleting: true);
        VerifyEntry(source);
        VerifyEntry(Path.GetDirectoryName(destination)!);
        if (expectedIdentity is not null) { SpeechComponentFileBinding.RequireIdentity(handle, expectedIdentity); }
        SpeechComponentFileBinding.Rename(handle, pinned.Last, Path.GetFileName(destination));
        VerifyEntry(destination);
        lock (creation)
        {
            foreach (var path in createdFiles.Keys.Where(path =>
                path.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray())
            { createdFiles.Remove(path); }
        }
    }

    public void DeleteFile(string path)
    {
        string identity;
        lock (creation)
        {
            if (!createdFiles.TryGetValue(path, out identity!))
            { throw new UnauthorizedAccessException("Cleanup requires the exact file created by this storage owner."); }
        }
        using var pinned = PinDirectory(Path.GetDirectoryName(path)!);
        using var handle = SpeechComponentFileBinding.Open(path, directory: false, deleting: true);
        VerifyEntry(path);
        SpeechComponentFileBinding.RequireIdentity(handle, identity);
        SpeechComponentFileBinding.Delete(handle);
        lock (creation) { createdFiles.Remove(path); }
    }

    public void MoveFile(string source, string destination)
    {
        VerifyEntry(source);
        ValidateContained(destination);
        VerifyEntry(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: false);
        VerifyEntry(destination);
        lock (creation) { createdFiles.Remove(source); }
    }

    public void DeleteDirectory(string path, string? expectedIdentity = null)
    {
        using var pinned = PinDirectory(Path.GetDirectoryName(path)!);
        using var handle = SpeechComponentFileBinding.Open(path, directory: true, deleting: true);
        VerifyEntry(path);
        if (expectedIdentity is not null) { SpeechComponentFileBinding.RequireIdentity(handle, expectedIdentity); }
        SpeechComponentFileBinding.Delete(handle);
    }

    private void ValidateContained(string path)
    {
        directory.Verify();
        if (!Path.GetFullPath(path).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFullPath(path), Root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Speech data must remain inside its private data root.");
        }
    }

    private void VerifyEntry(string path)
    {
        ValidateContained(path);
        RestrictedStorageDirectory.RejectReparseAncestors(path);
        if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
        {
            directory.VerifyPermissions(new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
                requireProtected: true, allowSystemAdministrators: false);
        }
        else { directory.VerifyFile(path); }
    }

    private static bool EntryExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void RequireInteractiveUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!Environment.UserInteractive || identity.IsAnonymous || identity.IsSystem
            || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Optional speech data changes require the non-elevated interactive user.");
        }
    }
}