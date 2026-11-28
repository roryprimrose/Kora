using Kora.Core.Skills;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

public sealed partial class WindowsOwnedDeclarativeSkillStore
{
    private const string StagePrefix = ".owned-skill-";

    // Four attempts in total; each wait precedes one retry of the same unobserved rename.
    private static readonly TimeSpan[] CommitRetryDelays =
        [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)];

    private void CommitWithinBound(SafeFileHandle stage, SafeFileHandle parent, string version, Action revalidate,
        CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            var status = OwnedSkillPackagePublication.TryCommit(stage, parent, version);
            if (status == 0) { return; }
            if (!OwnedSkillPackagePublication.IsTransientCommitRefusal(status) || attempt > CommitRetryDelays.Length)
            { throw OwnedSkillPackagePublication.CommitFailure(status); }
            var delay = CommitRetryDelays[attempt - 1];
            PublicationRetrying(logger, OwnedSkillPackagePublication.WindowsError(status), attempt + 1,
                CommitRetryDelays.Length + 1, (int)delay.TotalMilliseconds);
#pragma warning disable VSTHRD002 // Publication runs synchronously on a pool worker under the exclusive private lease.
            Task.Delay(delay, time, token).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            revalidate();
        }
    }

    private void RequireBoundRoot(SafeFileHandle root)
    {
        directory.Verify();
        OwnedSkillPackagePublication.Verify(root, directory.Root, directory: true);
        if (!string.Equals(WindowsFileIdentity.Read(root), rootIdentity, StringComparison.Ordinal))
        { throw new InvalidDataException("The bound owned skill root was replaced. Restart and inspect explicitly."); }
    }

    private void RetireStage(string stage, SafeFileHandle handle, Dictionary<string, string> createdFiles,
        Dictionary<string, string> createdDirectories, Exception? unpublished)
    {
        try { CleanupStage(stage, handle, createdFiles, createdDirectories); }
        catch (Exception cleanup) when (unpublished is not null
            && cleanup is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The publication failure stays authoritative; a later verified save reaps the retained private stage.
            StagingCleanupFailed(logger, cleanup.GetType().FullName!, unpublished.GetType().FullName!);
        }
    }

    private void ReapOrphanStages(string id, CancellationToken token)
    {
        var idPath = Path.Combine(directory.Root, id);
        if (!Directory.Exists(idPath)) { return; }
        using var idPin = new OwnedSkillPackagePublication();
        idPin.PinDirectory(idPath);
        directory.VerifyOwnedSkillDirectory(idPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(idPath).Take(SharedSkillCatalogue.MaximumPackages + 1).ToArray())
        {
            token.ThrowIfCancellationRequested();
            if (!IsKoraStageName(Path.GetFileName(entry))) { continue; }
            try { OrphanStagingReaped(logger, ReapStage(entry, token)); }
            catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException or InvalidDataException)
            { OrphanStagingRetained(logger, refusal.GetType().FullName!); }
        }
    }

    private static bool IsKoraStageName(string name) =>
        name.Length == StagePrefix.Length + 32 && name.StartsWith(StagePrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name.AsSpan(StagePrefix.Length), "N", out var value)
        && name.AsSpan(StagePrefix.Length).SequenceEqual(value.ToString("N"));

    private int ReapStage(string stage, CancellationToken token)
    {
        var directories = new List<(string Path, OwnedSkillPackagePublication Pin, SafeFileHandle Handle)>();
        var files = new List<SafeFileHandle>();
        try
        {
            // Pin and verify the complete bounded tree before any deletion: one refusal leaves every entry in place.
            // Held handles deny replacement, and only verified owned non-reparse single-link entries are retired.
            AddDirectory(stage);
            for (var index = 0; index < directories.Count; index++)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directories[index].Path)
                    .Take(SharedSkillCatalogue.MaximumEntries + 1).ToArray())
                {
                    token.ThrowIfCancellationRequested();
                    if (directories.Count + files.Count >= SharedSkillCatalogue.MaximumEntries)
                    { throw new InvalidDataException("Orphaned private staging exceeds its entry bound; nothing was deleted."); }
                    if (File.GetAttributes(entry).HasFlag(FileAttributes.Directory)) { AddDirectory(entry); }
                    else
                    {
                        files.Add(OwnedSkillPackagePublication.OpenForDeletion(entry));
                        directory.VerifyFile(entry);
                    }
                }
            }
            foreach (var file in files)
            {
                OwnedSkillPackagePublication.MarkDeleted(file);
                file.Dispose();
            }
            for (var index = directories.Count - 1; index >= 0; index--)
            {
                var (path, pin, handle) = directories[index];
                if (Directory.EnumerateFileSystemEntries(path).Any())
                { throw new InvalidDataException("Unexpected staging data was retained; no external entry was deleted."); }
                OwnedSkillPackagePublication.MarkDeleted(handle);
                pin.Dispose();
            }
            return directories.Count + files.Count;
        }
        finally
        {
            foreach (var file in files) { file.Dispose(); }
            foreach (var held in directories) { held.Pin.Dispose(); }
        }

        void AddDirectory(string path)
        {
            var pin = new OwnedSkillPackagePublication();
            try { directories.Add((path, pin, pin.PinDirectory(path, forRename: true))); }
            catch { pin.Dispose(); throw; }
            directory.VerifyOwnedSkillDirectory(path);
        }
    }
}
