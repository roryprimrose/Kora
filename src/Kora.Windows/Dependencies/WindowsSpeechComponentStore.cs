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
            return pending with { Installed = false, Ownership = SpeechComponentOwnership.RemovalPending };
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
            files.MoveDirectory(staging, ComponentPath(component));
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
                files.DeleteDirectory(staging);
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
        var observed = await InspectAsync(component, cancellationToken).ConfigureAwait(false);
        RequireExpected(expected, observed);
        if (observed.Ownership == SpeechComponentOwnership.Missing)
        {
            return;
        }
        if (observed.Ownership == SpeechComponentOwnership.Foreign)
        {
            throw new UnauthorizedAccessException("Foreign, modified-manifest or shared speech assets cannot be removed.");
        }
        var removing = ComponentPath(component) + ".removing";
        var removalRecord = ComponentPath(component) + ".removal-record";
        RequireAdmission(component, admitted, cancellationToken);
        if (observed.Ownership == SpeechComponentOwnership.Managed)
        {
            files.MoveDirectory(ComponentPath(component), removing);
        }
        if (!files.Exists(removalRecord))
        {
            var temporaryRecord = removalRecord + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            try
            {
                await using (var output = files.Create(temporaryRecord))
                {
                    await output.WriteAsync(Encoding.ASCII.GetBytes(component.ManifestDigest), cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
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
            foreach (var asset in component.Assets)
            {
                RequireAdmission(component, admitted, cancellationToken);
                var path = Path.Combine(removing, asset.Name);
                if (files.Exists(path)) { files.DeleteFile(path); }
            }
            RequireAdmission(component, admitted, cancellationToken);
            var marker = Path.Combine(removing, ManifestName);
            if (files.Exists(marker)) { files.DeleteFile(marker); }
            files.DeleteDirectory(removing);
        }
        RequireAdmission(component, admitted, cancellationToken);
        files.DeleteFile(removalRecord);
    }

    private async Task<SpeechComponentSnapshot> InspectDirectoryAsync(SpeechComponentDefinition component,
        string path, CancellationToken cancellationToken, bool removalRecorded = false)
    {
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
        var stamps = new List<string> { component.ManifestDigest };
        foreach (var asset in component.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assetPath = Path.Combine(path, asset.Name);
            if (!files.Exists(assetPath))
            {
                verified = false;
                stamps.Add(asset.Name + ":missing");
                continue;
            }
            await using var input = files.Read(assetPath);
            var length = input.Length;
            var digest = length <= asset.SizeBytes
                ? Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false))
                : "oversize";
            verified &= length == asset.SizeBytes && string.Equals(digest, asset.Sha256, StringComparison.Ordinal);
            stamps.Add(asset.Name + ":" + length.ToString(CultureInfo.InvariantCulture) + ":" + digest);
        }
        return new(true, verified, SpeechComponentOwnership.Managed,
            Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('|', stamps)))));
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
        new(false, false, SpeechComponentOwnership.Missing, "missing-" + component.ManifestDigest);
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
