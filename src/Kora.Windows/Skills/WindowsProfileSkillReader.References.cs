using Kora.Core.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.Skills;

public sealed partial class WindowsProfileSkillReader
{
    public ValueTask<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSource source, SharedSkillSnapshot primary,
        CancellationToken cancellationToken) => new(Task.Run(async () =>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(primary);
        if (primary.SourceId != source.Id || primary.ReferenceDeclaration is null || primary.PackageDirectoryIdentity is null)
        { throw new SharedSkillUnavailableException("exact-pinned-declaration-required"); }
        cancellationToken.ThrowIfCancellationRequested();
        var root = ValidateAbsolutePath(Path.Combine(profile, source.ProfileRelativeRoot));
        using var sourcePins = PinAncestors(root);
        if (!string.Equals(WindowsFileIdentity.Read(sourcePins.Last), source.DirectoryIdentity, StringComparison.Ordinal))
        { throw new SharedSkillUnavailableException("registered-source-replaced"); }
        var file = ValidateAbsolutePath(Path.Combine(root, primary.RelativeFile));
        var directory = Path.GetDirectoryName(file)!;
        using var packagePins = PinAncestors(directory);
        if (!string.Equals(WindowsFileIdentity.Read(packagePins.Last), primary.PackageDirectoryIdentity, StringComparison.Ordinal))
        { throw new SharedSkillUnavailableException("selected-package-replaced"); }
        var entries = new List<string>();
        Inventory(directory, 0, packagePins, entries, cancellationToken);
        var others = entries.Where(entry => !string.Equals(entry, file, StringComparison.Ordinal))
            .Select(entry => Path.GetRelativePath(root, entry)).Order(StringComparer.Ordinal).ToArray();
        if (!primary.ReferenceDeclaration.MatchesInventory(primary.RelativeFile, others))
        { throw new SharedSkillUnavailableException("declared-package-entries-changed"); }
        var bytes = await ReadCompleteAsync(file, SharedSkillSnapshot.MaximumBytes, cancellationToken).ConfigureAwait(false);
        if (!bytes.AsSpan().SequenceEqual(primary.Bytes))
        { throw new SharedSkillUnavailableException("primary-revision-changed"); }
        var candidate = SharedSkillSnapshot.Parse(source.Id, primary.RelativeFile, bytes, true, others)
            .WithPackageDirectoryIdentity(primary.PackageDirectoryIdentity);
        if (!candidate.IsReferenceCandidate)
        { throw new SharedSkillUnavailableException("reference-declaration-unavailable"); }
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var total = bytes.Length;
        foreach (var path in candidate.ReferenceDeclaration!.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = ValidateAbsolutePath(Path.Combine(directory, path));
            var content = await ReadCompleteAsync(reference,
                Math.Min(SharedSkillReferenceDeclaration.MaximumReferenceBytes,
                    SharedSkillReferenceDeclaration.MaximumBundleBytes - total), cancellationToken).ConfigureAwait(false);
            total += content.Length;
            contents.Add(path, content);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = candidate.WithReferenceBundle(SharedSkillReferenceBundle.Create(candidate, contents));
        if (primary.ReferenceBundle is not null && !string.Equals(primary.RevisionDigest, result.RevisionDigest, StringComparison.Ordinal))
        { throw new SharedSkillUnavailableException("reference-revision-changed"); }
        return result;
    }, cancellationToken));

    private static async Task<byte[]> ReadCompleteAsync(string path, int maximum, CancellationToken token)
    {
        using var handle = OpenPinned(path, directory: false, content: true);
        await using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: true);
        if (stream.Length <= 0 || stream.Length > maximum)
        { throw new SharedSkillUnavailableException("complete-reference-byte-limit"); }
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Position != stream.Length)
        { throw new SharedSkillUnavailableException("reference-changed-during-read"); }
        return bytes;
    }
}
