using System.Collections.Frozen;
using System.Security.Cryptography;
using Kora.Core.Configuration;

using Kora.Windows.Storage;

namespace Kora.Windows.Audio;

/// <summary>Compiled host metadata, never a user/model-authored manifest or recording authority.</summary>
internal sealed class HandsFreeNativeProfile
{
    public HandsFreeNativeProfile(string nativeLibraryPath, string modelDirectory, string dictionaryPath,
        string vadPath, IEnumerable<HandsFreeNativeAsset> assets, IEnumerable<string> names, string qualification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualification);
        NativeLibraryPath = RequireAbsolute(nativeLibraryPath);
        ModelDirectory = RequireAbsolute(modelDirectory);
        DictionaryPath = RequireAbsolute(dictionaryPath);
        VadPath = RequireAbsolute(vadPath);
        var files = assets.ToArray();
        if (files.Length is < 4 or > 32 || files.Any(asset => asset.Length <= 0
            || asset.Sha256.Length != 64 || asset.Sha256.Any(character => !char.IsAsciiHexDigit(character))
            || !string.Equals(RequireAbsolute(asset.Path), asset.Path, StringComparison.Ordinal))
            || files.Select(asset => asset.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length
            || new[] { NativeLibraryPath, DictionaryPath, VadPath }.Any(path =>
                !files.Any(asset => string.Equals(asset.Path, path, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ArgumentException("Exact complete native/model/VAD identities are required.", nameof(assets));
        }
        Assets = Array.AsReadOnly(files);
        Names = names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        if (Names.Count == 0 || Names.Any(name =>
            !string.Equals(AssistantNameRules.Normalize(name), name, StringComparison.Ordinal)
            || name.Any(character => !char.IsAscii(character))))
        {
            throw new ArgumentException("An exact qualified English configured-name set is required.", nameof(names));
        }
    }

    public string NativeLibraryPath { get; }
    public string ModelDirectory { get; }
    public string DictionaryPath { get; }
    public string VadPath { get; }
    public IReadOnlyList<HandsFreeNativeAsset> Assets { get; }
    public FrozenSet<string> Names { get; }

    public async Task<HandsFreeAssetLease> BindAsync(CancellationToken cancellationToken)
    {
        var streams = new List<FileStream>();
        try
        {
            RestrictedStorageDirectory.RejectReparseAncestors(ModelDirectory);
            foreach (var path in Directory.EnumerateFiles(ModelDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!Assets.Any(asset => string.Equals(asset.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("The wake model directory contains unadmitted data.");
                }
            }
            if (Directory.EnumerateDirectories(ModelDirectory).Any())
            {
                throw new InvalidDataException("The wake model directory cannot contain nested model roots.");
            }
            var nativeDirectory = Path.GetDirectoryName(NativeLibraryPath)!;
            RestrictedStorageDirectory.RejectReparseAncestors(nativeDirectory);
            foreach (var path in Directory.EnumerateFiles(nativeDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                if (!Assets.Any(asset => string.Equals(asset.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("The native dependency directory contains an unadmitted library.");
                }
            }
            foreach (var asset in Assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RestrictedStorageDirectory.RejectReparseAncestors(asset.Path);
                var stream = new FileStream(asset.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                streams.Add(stream);
                if (stream.Length != asset.Length || !string.Equals(
                    Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)),
                    asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The exact admitted hands-free assets failed verification.");
                }
            }
            return new(streams);
        }
        catch
        {
            foreach (var stream in streams) { await stream.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private static string RequireAbsolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.GetFullPath(path) : throw new ArgumentException("Host asset paths must be absolute.", nameof(path));
}
