using Kora.Core.Customizations;

namespace Kora.Windows.Artifacts;

public sealed partial class WindowsCustomizationStore
{
    internal Action? BeforeDeletionComparison { get; set; }
    internal Action? AfterDeletionCommitted { get; set; }

    public Task Delete(CustomizationSnapshot reviewed, Func<bool> eligible,
        Func<Action, Task> commit, CancellationToken token)
    {
        CustomizationDeletion.Validate(reviewed);
        return Task.Run(async () => await commit(() =>
        {
            lock (gate) { DeleteCore(reviewed, eligible, token); }
        }).ConfigureAwait(false), token);
    }

    private void DeleteCore(CustomizationSnapshot reviewed, Func<bool> eligible, CancellationToken token)
    {
        var target = Resolve(reviewed.Source);
        var ownedRoot = Path.Combine(paths.RoamingRoot,
            target.Source.Kind == CustomizationKind.Agent ? "Agents" : target.Source.Kind + "s");
        if (!string.Equals(target.Directory, ownedRoot, StringComparison.Ordinal)
            || !string.Equals(target.Source.Location, Path.Combine(ownedRoot, target.FileName), StringComparison.Ordinal))
        { throw new InvalidDataException("Deletion is confined to the exact host-owned kind root."); }
        using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(ownedRoot, false, token);
        RequireDeletionCurrent(target, reviewed, token);
        var committed = false;
        try
        {
            WindowsOwnedDefinitionFileAccess.DeleteReviewedDefinitionFile(target.Source.Location, () =>
            {
                BeforeDeletionComparison?.Invoke();
                RequireDeletionCurrent(target, reviewed, token);
            }, eligible, () => committed = true, token);
            AfterDeletionCommitted?.Invoke();
            RequireEligible(eligible, token);
            if (Capture(target, token).Files.Count != 0)
            { throw new IOException("The source path is occupied after deletion; no replacement was deleted."); }
        }
        catch (Exception exception) when (committed && exception is not OutOfMemoryException)
        { throw new CustomizationDeletionUnconfirmedException(exception); }
    }

    private void RequireDeletionCurrent(Target target, CustomizationSnapshot reviewed, CancellationToken token)
    {
        if (File.Exists(JournalPath(target)))
        { throw new InvalidOperationException("Recover the interrupted source transaction before deletion."); }
        var current = Capture(target, token);
        if (!string.Equals(current.Revision, reviewed.Revision, StringComparison.Ordinal)
            || current.Files.Count != 1
            || !current.Files[0].Bytes.AsSpan().SequenceEqual(reviewed.Files.Single().CopyBytes()))
        { throw new InvalidOperationException("The reviewed source is changed, missing or replaced. Reload and review; nothing was deleted."); }
    }
}
