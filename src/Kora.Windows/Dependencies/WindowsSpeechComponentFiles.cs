using System.Security.AccessControl;
using System.Security.Principal;
using Kora.Core.Dependencies;
using Kora.Windows.Storage;

namespace Kora.Windows.Dependencies;

public sealed class WindowsSpeechComponentFiles(IApplicationDataPaths paths) : ISpeechComponentFiles
{
    private readonly RestrictedStorageDirectory directory = new(paths, includeKeys: false, partitionName: "SpeechComponentsV1");

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
        VerifyEntry(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
    }

    public Stream Create(string path)
    {
        ValidateContained(path);
        VerifyEntry(Path.GetDirectoryName(path)!);
        return directory.CreateNewFile(path, FileOptions.Asynchronous);
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

    public void MoveDirectory(string source, string destination)
    {
        VerifyEntry(source);
        ValidateContained(destination);
        VerifyEntry(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
        VerifyEntry(destination);
    }

    public void DeleteFile(string path)
    {
        VerifyEntry(path);
        File.Delete(path);
    }

    public void MoveFile(string source, string destination)
    {
        VerifyEntry(source);
        ValidateContained(destination);
        VerifyEntry(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: false);
        VerifyEntry(destination);
    }

    public void DeleteDirectory(string path)
    {
        VerifyEntry(path);
        Directory.Delete(path, recursive: false);
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
