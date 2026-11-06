using System.Collections.Frozen;
using System.Security.Cryptography;

using Kora.Core.Configuration;

using Kora.Windows.Storage;
using Kora.Windows.Dependencies;

namespace Kora.Windows.Audio;

/// <summary>Compiled host metadata, never a user/model-authored manifest or recording authority.</summary>
internal sealed class HandsFreeNativeProfile
{
    public HandsFreeNativeProfile(string nativeLibraryPath, string modelDirectory, string dictionaryPath,
        string vadPath, IEnumerable<HandsFreeNativeAsset> assets, IEnumerable<string> names, string? qualification,
        IReadOnlyDictionary<string, string>? configurationFiles = null)
    {
        if (qualification is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(qualification); }
        CaptureQualified = qualification is not null;
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
        if ((CaptureQualified && Names.Count == 0) || Names.Any(name =>
            !string.Equals(AssistantNameRules.Normalize(name), name, StringComparison.Ordinal)
            || name.Any(character => !char.IsAscii(character))))
        {
            throw new ArgumentException("An exact qualified English configured-name set is required.", nameof(names));
        }
        ConfigurationFiles = (configurationFiles ?? new Dictionary<string, string>(StringComparer.Ordinal))
            .ToFrozenDictionary(StringComparer.Ordinal);
        if (ConfigurationFiles.Any(pair => pair.Key is not ("mdef" or "mean" or "var" or "tmat" or "sendump" or "fdict" or "featparams")
            || !Assets.Any(asset => string.Equals(asset.Path, pair.Value, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ArgumentException("Model configuration must refer only to exact bound data assets.", nameof(configurationFiles));
        }
    }

    public string NativeLibraryPath { get; }
    public string ModelDirectory { get; }
    public string DictionaryPath { get; }
    public string VadPath { get; }
    public IReadOnlyList<HandsFreeNativeAsset> Assets { get; }
    public FrozenSet<string> Names { get; }
    public bool CaptureQualified { get; }
    public IReadOnlyDictionary<string, string> ConfigurationFiles { get; }

    internal string ObserveNativeIdentity()
    {
        var asset = Assets.Single(candidate => string.Equals(candidate.Path, NativeLibraryPath, StringComparison.Ordinal));
        using var pinned = new SpeechComponentFileBinding();
        pinned.PinAncestors(Path.GetDirectoryName(NativeLibraryPath)!);
        if (Directory.EnumerateFiles(Path.GetDirectoryName(NativeLibraryPath)!, "*.dll").Any(path =>
            !Assets.Any(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase))))
        { throw new InvalidDataException("The native dependency inventory changed. Application repair is required."); }
        using var handle = SpeechComponentFileBinding.Open(NativeLibraryPath, directory: false);
        using var stream = new FileStream(handle, FileAccess.Read);
        if (stream.Length != asset.Length
            || !string.Equals(Convert.ToHexString(SHA256.HashData(stream)), asset.Sha256, StringComparison.Ordinal))
        { throw new InvalidDataException("The exact bundled speech-native identity is unavailable. Application repair is required."); }
        return WindowsFileIdentity.Read(handle);
    }

    public async Task<HandsFreeAssetLease> BindAsync(CancellationToken cancellationToken, IAsyncDisposable? ownership = null)
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
            return new(streams, ownership);
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