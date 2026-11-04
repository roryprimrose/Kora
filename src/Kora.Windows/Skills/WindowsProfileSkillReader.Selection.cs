using Kora.Core.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.Skills;

public sealed partial class WindowsProfileSkillReader
{
    public ValueTask<SharedSkillFileBinding> BindFileAsync(IReadOnlyList<SharedSkillSource> registered,
        string selectedFile, CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = ValidateAbsolutePath(selectedFile);
            var matches = registered.Where(source => file.StartsWith(
                Path.Combine(profile, source.ProfileRelativeRoot) + '\\', StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            { throw new SharedSkillUnavailableException("selected-file-registration-unavailable-or-ambiguous"); }
            var source = matches[0];
            var relative = Path.GetRelativePath(Path.Combine(profile, source.ProfileRelativeRoot), file);
            using var pins = PinSelectedFile(source, relative, cancellationToken);
            return CaptureBinding(source, relative, pins);
        }, cancellationToken));

    public ValueTask<SharedSkillSnapshot> ReadFileAsync(SharedSkillFileBinding binding, CancellationToken cancellationToken) =>
        new(Task.Run(async () =>
        {
            using var pins = PinSelectedFile(binding.Source, binding.RelativeFile, cancellationToken);
            if (CaptureBinding(binding.Source, binding.RelativeFile, pins) != binding)
            { throw new SharedSkillUnavailableException("selected-file-revision-changed"); }
            await using var stream = new FileStream(pins.Last, FileAccess.Read, 4096, isAsync: true);
            if (stream.Length is 0 or > SharedSkillSnapshot.MaximumBytes)
            { throw new SharedSkillUnavailableException("file-or-total-byte-limit"); }
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.Position != stream.Length || CaptureBinding(binding.Source, binding.RelativeFile, pins) != binding)
            { throw new SharedSkillUnavailableException("selected-file-revision-changed"); }
            return SharedSkillSnapshot.Parse(binding.Source.Id, binding.RelativeFile, bytes, false)
                .WithPackageDirectoryIdentity(binding.PackageIdentity);
        }, cancellationToken));

    private PinnedHandles PinSelectedFile(SharedSkillSource source, string relative, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = SharedSkillSource.ValidateRelativePath(relative);
        var parts = relative.Split('\\');
        if (parts.Length != 2 || parts[0] is "" or "." or ".."
            || !string.Equals(parts[1], "SKILL.md", StringComparison.Ordinal))
        { throw new SharedSkillUnavailableException("selected-single-file-profile-required"); }
        var root = ValidateAbsolutePath(Path.Combine(profile, source.ProfileRelativeRoot));
        var pins = PinAncestors(root);
        try
        {
            if (!string.Equals(WindowsFileIdentity.Read(pins.Last), source.DirectoryIdentity, StringComparison.Ordinal))
            { throw new SharedSkillUnavailableException("registered-source-replaced"); }
            var package = ValidateAbsolutePath(Path.Combine(root, parts[0]));
            pins.Add(OpenPinned(package, directory: true, content: false));
            var entries = Directory.EnumerateFileSystemEntries(package).Take(2).ToArray();
            var file = Path.Combine(package, "SKILL.md");
            if (entries.Length != 1 || !string.Equals(entries[0], file, StringComparison.Ordinal))
            { throw new SharedSkillUnavailableException("selected-single-file-profile-required"); }
            pins.Add(OpenPinned(file, directory: false, content: true));
            token.ThrowIfCancellationRequested();
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }

    private static SharedSkillFileBinding CaptureBinding(SharedSkillSource source, string relative, PinnedHandles pins) =>
        new(source, relative, WindowsFileIdentity.Read(pins.Previous), WindowsFileIdentity.Read(pins.Last),
            File.GetLastWriteTimeUtc(pins.Last).Ticks);
}
