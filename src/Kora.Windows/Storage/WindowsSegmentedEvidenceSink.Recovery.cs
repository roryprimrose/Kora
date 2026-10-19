using Kora.Core.Diagnostics;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSegmentedEvidenceSink
{
    public EvidenceCutoverRecoveryReceipt RecoverInterruptedFirstCutover(
        Action requireAuthority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requireAuthority);
        cancellationToken.ThrowIfCancellationRequested();
        requireAuthority();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (inventory is not null || failure is not null || segments.Count != 0)
            {
                throw new InvalidOperationException("Explicit cutover recovery requires a fresh, uninitialized evidence store.");
            }

            try
            {
                RequireUnpublishedCutoverShell();
                inventoryLease = directory.AcquireBoundedLease(requireExisting: true, cancellationToken);
                requireAuthority();
                RequireUnpublishedCutoverShell();
                if (inventoryLease.Length != 0)
                {
                    throw new InvalidDataException("The interrupted evidence admission lease is not empty.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                AdmitLegacy(cancellationToken);
                if (segments.Count != 1)
                {
                    throw new InvalidDataException("Interrupted first-cutover recovery requires retained legacy evidence.");
                }

                PopulateIdentityFilter(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                RequireUnpublishedCutoverShell();
                requireAuthority();
                CreateInventory(requireAuthority, cancellationToken);
                VerifyInventory();
                // Publication is committed: cancellation cannot relabel it as a rollback.
                return new(Guid.ParseExact(inventory!.StoreIdentity, "D"), inventory.Generation, segments.Count);
            }
            catch (Exception exception)
            {
                failure = exception;
                ReleaseOwnedResources();
                throw;
            }
        }
    }

    private void RequireUnpublishedCutoverShell()
    {
        if (!directory.HasExistingPartition())
        {
            throw new InvalidDataException("There is no existing interrupted evidence cutover to recover.");
        }

        directory.Verify();
        var entries = Directory.EnumerateFileSystemEntries(directory.Root).Take(3)
            .Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        if (!entries.SetEquals(["Artifacts", "operation.lock"])
            || Directory.EnumerateFileSystemEntries(directory.Artifacts).Any())
        {
            throw new InvalidDataException("Explicit evidence recovery accepts only an empty, never-published first-cutover shell. Existing files were preserved.");
        }
        directory.VerifyFile(Path.Combine(directory.Root, "operation.lock"));
    }
}
