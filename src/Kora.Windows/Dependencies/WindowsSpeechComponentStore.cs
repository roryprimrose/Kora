using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Dependencies;

public sealed class WindowsSpeechComponentStore(
    ISpeechComponentFiles files, ISpeechComponentAcquisition acquisition) : ISpeechComponentStore
{
    private const string ManifestName = ".kora-component-v1";

    public async Task<SpeechComponentSnapshot> InspectAsync(SpeechComponentDefinition component, CancellationToken cancellationToken)
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        try
        {
            var result = await InspectCoreAsync(component, cancellationToken).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw new IOException("Windows speech data ownership could not be inspected. No missing/ready state was inferred.", exception);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private async Task<SpeechComponentSnapshot> InspectCoreAsync(SpeechComponentDefinition component, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(component);
        cancellationToken.ThrowIfCancellationRequested();
        if (!files.RootExists()) { return Missing(component); }
        using var pinned = files.PinDirectory(files.Root);
        var path = ComponentPath(component);
        var removing = path + ".removing";
        var removalRecord = path + ".removal-record";
        var hasRecord = files.Exists(removalRecord);
        if (hasRecord && !await MatchesManifestAsync(component, removalRecord, cancellationToken).ConfigureAwait(false))
        {
            return new(files.Exists(path), false, SpeechComponentOwnership.Foreign, "foreign-removal-record-" + component.ManifestDigest);
        }
        if (files.Exists(removing) || hasRecord)
        {
            var pending = files.Exists(removing)
                ? await InspectDirectoryAsync(component, removing, cancellationToken, hasRecord).ConfigureAwait(false)
                : Missing(component);
            if (files.Exists(path) || pending.Ownership == SpeechComponentOwnership.Foreign)
            {
                return new(true, false, SpeechComponentOwnership.Foreign, "conflicting-" + pending.Stamp);
            }
            return pending with
            {
                Installed = false,
                Ownership = SpeechComponentOwnership.RemovalPending,
                ExactBytes = pending.ExactBytes + (hasRecord ? 64 : 0),
                Inventory = pending.Inventory + (hasRecord ? "\nRemoval recovery record: 64 bytes; exact catalogue manifest." : string.Empty),
                Stamp = pending.Stamp + (hasRecord ? files.Identity(removalRecord) : string.Empty),
            };
        }
        return files.Exists(path)
            ? await InspectDirectoryAsync(component, path, cancellationToken).ConfigureAwait(false)
            : Missing(component);
    }

    public Task PrepareAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected,
        Func<bool> admitted, IProgress<string> progress, CancellationToken cancellationToken) =>
        MutateAsync(() => PrepareCoreAsync(component, expected, admitted, progress, cancellationToken));

    private async Task PrepareCoreAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected,
        Func<bool> admitted, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(progress);
        RequireAdmission(component, admitted, cancellationToken);
        files.EnsureRoot();
        await using var lease = await files.AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);
        using var pinned = files.PinDirectory(files.Root);
        var observed = await InspectAsync(component, cancellationToken).ConfigureAwait(false);
        RequireExpected(expected, observed);
        if (observed is { Ownership: SpeechComponentOwnership.Managed, Verified: true })
        {
            return;
        }
        if (observed.Ownership != SpeechComponentOwnership.Missing)
        {
            throw new InvalidDataException("Existing or interrupted speech data is preserved. Explicitly review removal/reconciliation before preparing again.");
        }
        var staging = ComponentPath(component) + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".staging";
        files.CreateDirectory(staging);
        var stagingIdentity = files.Identity(staging);
        var owned = new List<string>();
        try
        {
            foreach (var asset in component.Assets)
            {
                RequireAdmission(component, admitted, cancellationToken);
                progress.Report("Downloading exact catalogued speech data.");
                var target = Path.Combine(staging, asset.Name);
                await using (var output = files.Create(target))
                {
                    owned.Add(target);
                    await acquisition.DownloadAsync(asset, output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                if (!await VerifyAssetAsync(asset, target, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("Speech component content did not match its exact size and SHA-256 digest.");
                }
            }
            var marker = Path.Combine(staging, ManifestName);
            await using (var output = files.Create(marker))
            {
                owned.Add(marker);
                await output.WriteAsync(Encoding.ASCII.GetBytes(component.ManifestDigest), cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            var verified = await InspectDirectoryAsync(component, staging, cancellationToken).ConfigureAwait(false);
            if (!verified.Verified) { throw new InvalidDataException("The complete staged component failed verification."); }
            RequireExpected(expected, await InspectAsync(component, cancellationToken).ConfigureAwait(false));
            RequireAdmission(component, admitted, cancellationToken);
            progress.Report("Publishing verified data atomically; runtime remains unselected.");
            RequireAdmission(component, admitted, cancellationToken);
            files.MoveDirectory(staging, ComponentPath(component), verified.DirectoryIdentity);
        }
        finally
        {
            // Never follow a guessed crash-stage path or recursively delete unknown content.
            if (files.Exists(staging))
            {
                foreach (var path in owned)
                {
                    if (files.Exists(path)) { files.DeleteFile(path); }
                }
                files.DeleteDirectory(staging, stagingIdentity);
            }
        }
    }

    public Task RemoveAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected,
        Func<bool> admitted, CancellationToken cancellationToken) =>
        MutateAsync(() => RemoveCoreAsync(component, expected, admitted, cancellationToken));

    private async Task RemoveCoreAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected,
        Func<bool> admitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(admitted);
        RequireAdmission(component, admitted, cancellationToken);
        if (!files.RootExists())
        {
            RequireExpected(expected, Missing(component));
            return;
        }
        await using var lease = await files.AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);
        using var pinned = files.PinDirectory(files.Root);
        var observed = await InspectAsync(component, cancellationToken).ConfigureAwait(false);
        RequireExpected(expected, observed);
        if (observed.Ownership == SpeechComponentOwnership.Missing)
        {
            return;
        }
        if (observed.Ownership == SpeechComponentOwnership.Foreign || observed.ExactBytes is null)
        {
            throw new UnauthorizedAccessException("Foreign, modified-manifest or shared speech assets cannot be removed.");
        }
        var removing = ComponentPath(component) + ".removing";
        var removalRecord = ComponentPath(component) + ".removal-record";
        var recordIdentity = files.Exists(removalRecord) ? files.Identity(removalRecord) : null;
        var source = observed.Ownership == SpeechComponentOwnership.Managed ? ComponentPath(component) : removing;
        var inventory = files.Exists(source)
            ? await ReadRemovalInventoryAsync(component, source, cancellationToken).ConfigureAwait(false) : [];
        if (!string.Equals(InventoryStamp(inventory), observed.FileInventoryStamp, StringComparison.Ordinal))
        { throw new InvalidDataException("The exact removal inventory changed during review revalidation."); }
        RequireExpected(expected, await InspectAsync(component, cancellationToken).ConfigureAwait(false));
        RequireAdmission(component, admitted, cancellationToken);
        if (observed.Ownership == SpeechComponentOwnership.Managed)
        {
            files.MoveDirectory(ComponentPath(component), removing, observed.DirectoryIdentity);
        }
        if (recordIdentity is null)
        {
            var temporaryRecord = removalRecord + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            try
            {
                await using (var output = files.Create(temporaryRecord))
                {
                    await output.WriteAsync(Encoding.ASCII.GetBytes(component.ManifestDigest), cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                recordIdentity = files.Identity(temporaryRecord);
                RequireAdmission(component, admitted, cancellationToken);
                files.MoveFile(temporaryRecord, removalRecord);
            }
            finally
            {
                if (files.Exists(temporaryRecord)) { files.DeleteFile(temporaryRecord); }
            }
        }
        // Cancellation after retirement leaves an exact tombstone for a fresh explicit removal review.
        if (files.Exists(removing))
        {
            using (files.PinDirectory(removing))
            {
                foreach (var entry in inventory.OrderBy(entry => string.Equals(entry.Name, ManifestName, StringComparison.Ordinal)))
                {
                    RequireAdmission(component, admitted, cancellationToken);
                    await files.DeleteVerifiedFileAsync(Path.Combine(removing, entry.Name), entry.Identity,
                        entry.Bytes, entry.Digest, admitted, cancellationToken).ConfigureAwait(false);
                }
            }
            RequireAdmission(component, admitted, cancellationToken);
            files.DeleteDirectory(removing, observed.DirectoryIdentity);
        }
        RequireAdmission(component, admitted, cancellationToken);
        await files.DeleteVerifiedFileAsync(removalRecord, recordIdentity, 64,
            Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(component.ManifestDigest))),
            admitted, cancellationToken).ConfigureAwait(false);
    }

    private sealed record RemovalFile(string Name, string Identity, long Bytes, string Digest);

    private static string InventoryStamp(IEnumerable<RemovalFile> entries) =>
        string.Join('|', entries.OrderBy(entry => entry.Name, StringComparer.Ordinal).Select(entry =>
            $"{entry.Name}:{entry.Identity}:{entry.Bytes.ToString(CultureInfo.InvariantCulture)}:{entry.Digest}"));

    private async Task<List<RemovalFile>> ReadRemovalInventoryAsync(SpeechComponentDefinition component,
        string path, CancellationToken cancellationToken)
    {
        using var pinned = files.PinDirectory(path);
        var result = new List<RemovalFile>();
        foreach (var name in files.Names(path))
        {
            var asset = component.Assets.SingleOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (asset is null && !string.Equals(name, ManifestName, StringComparison.Ordinal))
            { throw new InvalidDataException("Uninventoried data appeared during removal. It was not opened or deleted."); }
            var file = Path.Combine(path, name);
            var identity = files.Identity(file);
            await using var input = files.Read(file);
            var bytes = input.Length;
            if (bytes > (asset?.SizeBytes ?? 64))
            { throw new InvalidDataException("Speech data changed beyond its bounded inventory. Inspect before review."); }
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(files.Identity(file), identity, StringComparison.Ordinal))
            { throw new InvalidDataException("A speech-data object changed during inventory."); }
            result.Add(new(name, identity, bytes, digest));
        }
        return result;
    }

    private async Task<SpeechComponentSnapshot> InspectDirectoryAsync(SpeechComponentDefinition component,
        string path, CancellationToken cancellationToken, bool removalRecorded = false)
    {
        using var pinned = files.PinDirectory(path);
        var directoryIdentity = files.Identity(path);
        var names = files.Names(path);
        var marker = Path.Combine(path, ManifestName);
        if ((!removalRecorded && !names.Contains(ManifestName, StringComparer.Ordinal))
            || names.Any(name => !string.Equals(name, ManifestName, StringComparison.Ordinal)
                && !component.Assets.Any(asset => string.Equals(asset.Name, name, StringComparison.Ordinal))))
        {
            return new(true, false, SpeechComponentOwnership.Foreign, "foreign-" + component.ManifestDigest);
        }
        if (names.Contains(ManifestName, StringComparer.Ordinal)
            && !await MatchesManifestAsync(component, marker, cancellationToken).ConfigureAwait(false))
        {
            return new(true, false, SpeechComponentOwnership.Foreign, "manifest-" + component.ManifestDigest);
        }
        var verified = true;
        var stamps = new List<string> { component.ManifestDigest, files.Identity(files.Root), directoryIdentity };
        var inventory = new List<string>();
        var entries = new List<RemovalFile>();
        long bytes = 0;
        var known = true;
        if (names.Contains(ManifestName, StringComparer.Ordinal))
        {
            stamps.Add(files.Identity(marker));
            bytes = 64;
            inventory.Add("Ownership receipt: 64 bytes; exact catalogue manifest/source binding.");
            entries.Add(new(ManifestName, files.Identity(marker), 64,
                Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(component.ManifestDigest)))));
        }
        foreach (var asset in component.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assetPath = Path.Combine(path, asset.Name);
            if (!files.Exists(assetPath))
            {
                verified = false;
                stamps.Add(asset.Name + ":missing");
                inventory.Add(asset.Name + ": absent");
                continue;
            }
            await using var input = files.Read(assetPath);
            var identity = files.Identity(assetPath);
            var length = input.Length;
            var digest = length <= asset.SizeBytes
                ? Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false))
                : "oversize";
            verified &= length == asset.SizeBytes && string.Equals(digest, asset.Sha256, StringComparison.Ordinal);
            known &= length <= asset.SizeBytes;
            bytes = checked(bytes + length);
            inventory.Add($"{asset.Name}: {length} bytes; SHA-256 {digest}");
            stamps.Add(asset.Name + ":" + length.ToString(CultureInfo.InvariantCulture) + ":" + digest + ":" + identity);
            entries.Add(new(asset.Name, identity, length, digest));
        }
        return new(true, verified, SpeechComponentOwnership.Managed,
            Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('|', stamps)))))
        {
            ExactBytes = known ? bytes : null,
            Inventory = known ? string.Join('\n', inventory) : null,
            DirectoryIdentity = directoryIdentity,
            FileInventoryStamp = InventoryStamp(entries),
        };
    }

    private async Task<bool> VerifyAssetAsync(SpeechComponentAsset asset, string path, CancellationToken cancellationToken)
    {
        await using var input = files.Read(path);
        return input.Length == asset.SizeBytes
            && string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)),
                asset.Sha256, StringComparison.Ordinal);
    }

    private async Task<bool> MatchesManifestAsync(SpeechComponentDefinition component, string path, CancellationToken cancellationToken)
    {
        await using var input = files.Read(path);
        var bytes = new byte[65];
        var read = await input.ReadAtLeastAsync(bytes, 65, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        return read == 64 && string.Equals(Encoding.ASCII.GetString(bytes, 0, 64), component.ManifestDigest, StringComparison.Ordinal);
    }

    private string ComponentPath(SpeechComponentDefinition component) => Path.Combine(files.Root, component.Identity.StorageName);

    private static async Task MutateAsync(Func<Task> operation)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        try
        {
            await operation().ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw new IOException("The private speech data operation failed Windows ownership or storage validation. Inspect before retrying.", exception);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
    private static SpeechComponentSnapshot Missing(SpeechComponentDefinition component) =>
        new(false, false, SpeechComponentOwnership.Missing, "missing-" + component.ManifestDigest)
        { ExactBytes = 0, Inventory = "No model-data files remain.", FileInventoryStamp = string.Empty };
    private static void RequireExpected(SpeechComponentSnapshot expected, SpeechComponentSnapshot observed)
    {
        if (expected != observed) { throw new InvalidDataException("The reviewed component identity/content changed. Inspect before retrying."); }
    }
    private static void RequireAdmission(SpeechComponentDefinition component, Func<bool> admitted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!component.Qualified || !admitted()) { throw new UnauthorizedAccessException("Speech component consent or host admission is no longer current."); }
    }
}