using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Windows.Audio;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Audio;

// Evaluation metadata is test-local. It must never enter production composition or imply qualification.
internal sealed class HandsFreeOfflineFixture : IAsyncDisposable
{
    internal static readonly ImmutableArray<string> CaseIds =
        ["immediate", "paused", "no-command", "similar", "negative", "repeated", "chunk-split"];
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
    };
    private readonly NativeAssets assets;
    private readonly SpeechComponentFileBinding roots;
    private readonly ImmutableDictionary<string, FileStream> corpus;
    private Task? release;
    private bool sessionOwned;

    private HandsFreeOfflineFixture(Manifest manifest, string manifestDigest, HandsFreeNativeProfile profile,
        NativeAssets assets, ImmutableDictionary<string, FileStream> corpus, SpeechComponentFileBinding roots)
    {
        Specification = manifest;
        ManifestDigest = manifestDigest;
        Profile = profile;
        this.assets = assets;
        this.corpus = corpus;
        this.roots = roots;
    }

    internal Manifest Specification { get; }
    internal string ManifestDigest { get; }
    internal HandsFreeNativeProfile Profile { get; }

    internal static async Task<HandsFreeOfflineFixture> AdmitSelected(CancellationToken cancellationToken)
    {
        var path = Environment.GetEnvironmentVariable("KORA_OFFLINE_MANIFEST");
        var digest = Environment.GetEnvironmentVariable("KORA_OFFLINE_MANIFEST_SHA256");
        if (string.IsNullOrWhiteSpace(path) || !IsDigest(digest))
        {
            throw new InvalidOperationException("Explicit evaluation requires an absolute KORA_OFFLINE_MANIFEST and independently approved KORA_OFFLINE_MANIFEST_SHA256.");
        }
        return await Admit(path, digest, cancellationToken);
    }

    internal static async Task<HandsFreeOfflineFixture> Admit(string path, string digest, CancellationToken cancellationToken)
    {
        RequirePlainAbsolutePath(path);
        if (!IsDigest(digest)) { throw new InvalidDataException("A SHA-256 manifest identity is required."); }
        using var manifestRoot = new SpeechComponentFileBinding();
        manifestRoot.PinAncestors(Path.GetDirectoryName(path)!);
        await using var manifestStream = Open(path);
        if (manifestStream.Length is <= 0 or > 65536) { throw new InvalidDataException("The finite evaluation manifest exceeds its byte bound."); }
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(manifestStream, cancellationToken));
        if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The selected evaluation manifest identity changed.");
        }
        manifestStream.Position = 0;
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(manifestStream, ManifestOptions, cancellationToken)
            ?? throw new InvalidDataException("The evaluation manifest is absent.");
        Validate(manifest);
        var corpusFiles = ImmutableDictionary.CreateBuilder<string, FileStream>(StringComparer.Ordinal);
        var pinnedRoots = new SpeechComponentFileBinding();
        NativeAssets? assetLease = null;
        try
        {
            assetLease = await AdmitNativeAssets(manifest.AssetsRoot, manifest.NativePath, cancellationToken);
            pinnedRoots.PinAncestors(manifest.CorpusRoot);
            RequireInventory(manifest.CorpusRoot, manifest.Cases.Select(item => item.File));
            foreach (var item in manifest.Cases)
            {
                var stream = Open(Path.Combine(manifest.CorpusRoot, item.File));
                corpusFiles.Add(item.Id, stream);
                if (stream.Length != item.Bytes || !string.Equals(
                    Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)), item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The selected PCM fixture identity changed.");
                }
            }
            return new(manifest, actual, assetLease.Profile, assetLease, corpusFiles.ToImmutable(), pinnedRoots);
        }
        catch
        {
            foreach (var stream in corpusFiles.Values) { await stream.DisposeAsync(); }
            if (assetLease is not null) { await assetLease.DisposeAsync(); }
            pinnedRoots.Dispose();
            throw;
        }
    }

    internal static Task<NativeAssets> AdmitSelectedNativeAssets(CancellationToken cancellationToken)
    {
        var root = Environment.GetEnvironmentVariable("KORA_OFFLINE_ASSETS_ROOT");
        var native = Environment.GetEnvironmentVariable("KORA_OFFLINE_NATIVE_PATH");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(native))
        { throw new InvalidOperationException("Explicit native smoke requires KORA_OFFLINE_ASSETS_ROOT and KORA_OFFLINE_NATIVE_PATH."); }
        return AdmitNativeAssets(root, native, cancellationToken);
    }

    internal static async Task<NativeAssets> AdmitNativeAssets(string assetsRoot, string nativePath,
        CancellationToken cancellationToken)
    {
        RequirePlainAbsolutePath(assetsRoot);
        RequirePlainAbsolutePath(nativePath);
        if (!string.Equals(Path.GetFileName(nativePath), HandsFreeSetupCatalogue.NativeFileName, StringComparison.Ordinal))
        { throw new InvalidDataException("The exact bundled native filename is required."); }
        var definitions = HandsFreeSetupCatalogue.Component.Assets;
        var identities = definitions.Select(asset => new HandsFreeNativeAsset(
            Path.Combine(assetsRoot, asset.Name), asset.SizeBytes, asset.Sha256)).ToList();
        identities.Add(new(nativePath, HandsFreeSetupCatalogue.NativeBytes, HandsFreeSetupCatalogue.NativeSha256));
        var configuration = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mdef"] = Path.Combine(assetsRoot, "mdef.data"),
            ["mean"] = Path.Combine(assetsRoot, "means.data"),
            ["var"] = Path.Combine(assetsRoot, "variances.data"),
            ["tmat"] = Path.Combine(assetsRoot, "transition.data"),
            ["sendump"] = Path.Combine(assetsRoot, "sendump.data"),
            ["fdict"] = Path.Combine(assetsRoot, "noise.data"),
            ["featparams"] = Path.Combine(assetsRoot, "features.data"),
        };
        var profile = new HandsFreeNativeProfile(nativePath, assetsRoot,
            Path.Combine(assetsRoot, "dictionary.data"), Path.Combine(assetsRoot, "silero.onnx"),
            identities, ["Kora"], "EVALUATION ONLY: no acoustic qualification or production capture authority", configuration,
            keywordThreshold: HandsFreeNativeProfileCatalogue.KoraKeywordThreshold);
        var pinnedRoots = new SpeechComponentFileBinding();
        HandsFreeAssetLease? assetLease = null;
        try
        {
            pinnedRoots.PinAncestors(assetsRoot);
            pinnedRoots.PinAncestors(Path.GetDirectoryName(nativePath)!);
            RequireInventory(assetsRoot, definitions.Select(asset => asset.Name));
            RequireInventory(Path.GetDirectoryName(nativePath)!, [HandsFreeSetupCatalogue.NativeFileName]);
            assetLease = await profile.BindAsync(cancellationToken);
            return new(profile, assetLease, pinnedRoots);
        }
        catch
        {
            if (assetLease is not null) { await assetLease.DisposeAsync(); }
            pinnedRoots.Dispose();
            throw;
        }
    }

    internal static void Validate(Manifest manifest)
    {
        if (manifest.Version != 1 || manifest.Cases.IsDefault || manifest.Cases.Length != CaseIds.Length
            || manifest.Cases.Any(item => item is null)
            || !manifest.Cases.Select(item => item.Id).Order(StringComparer.Ordinal)
                .SequenceEqual(CaseIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The manifest must declare exactly the seven fixed offline cases.");
        }
        RequirePlainAbsolutePath(manifest.AssetsRoot);
        RequirePlainAbsolutePath(manifest.NativePath);
        RequirePlainAbsolutePath(manifest.CorpusRoot);
        if (!string.Equals(Path.GetFileName(manifest.NativePath), HandsFreeSetupCatalogue.NativeFileName, StringComparison.Ordinal)
            || manifest.Cases.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != CaseIds.Length)
        {
            throw new InvalidDataException("Exact native and unique corpus filenames are required.");
        }
        foreach (var item in manifest.Cases)
        {
            var positive = item.Id is not ("similar" or "negative");
            if (!string.Equals(item.File, item.Id + ".pcm", StringComparison.Ordinal) || !IsDigest(item.Sha256)
                || item.Bytes is <= 0 or > 960000 || item.Bytes % 2 != 0
                || item.ChunkSamples != (item.Id is "chunk-split" ? 127 : 320)
                || item.FirstFrameMinimum < 0 || item.FirstFrameMaximum < item.FirstFrameMinimum
                || item.LastFrameMinimum < item.FirstFrameMinimum || item.LastFrameMaximum < item.LastFrameMinimum
                || item.FirstFrameMaximum > item.LastFrameMaximum
                || (positive && ((long)item.LastFrameMaximum + 1) * 160 >= item.Bytes / 2)
                || (!positive && (item.FirstFrameMinimum != 0 || item.FirstFrameMaximum != 0
                    || item.LastFrameMinimum != 0 || item.LastFrameMaximum != 0))
                || item.CommandEndMinimum < 0 || item.CommandEndMaximum < item.CommandEndMinimum
                || item.CommandEndMaximum > item.Bytes / 2
                || (positive && (item.CommandEndMinimum <= ((long)item.LastFrameMaximum + 1) * 160
                    || item.CommandEndMaximum >= item.Bytes / 2))
                || (!positive && (item.CommandEndMinimum != 0 || item.CommandEndMaximum != 0)))
            {
                throw new InvalidDataException("An offline case violates its exact PCM16/16-kHz/mono, chunk or annotation bounds.");
            }
        }
    }

    internal async Task<short[]> ReadPcm(string id, CancellationToken cancellationToken)
    {
        var stream = corpus[id];
        stream.Position = 0;
        var bytes = new byte[checked((int)stream.Length)];
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            return MemoryMarshal.Cast<byte, short>(bytes).ToArray();
        }
        finally { Array.Clear(bytes); }
    }

    internal FileMicrophone Microphone(string id)
    {
        var specification = Specification.Cases.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        return new(Open(Path.Combine(Specification.CorpusRoot, specification.File)), specification.ChunkSamples);
    }

    private static FileStream Open(string path)
    {
        RequirePlainAbsolutePath(path);
        if (!File.Exists(path)) { throw new FileNotFoundException("A selected offline input is unavailable.", path); }
        return new(SpeechComponentFileBinding.Open(path, directory: false), FileAccess.Read, 81920, isAsync: false);
    }

    private static void RequirePlainAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.StartsWith(@"\\", StringComparison.Ordinal)
            || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)
            || new DriveInfo(Path.GetPathRoot(path)!).DriveType is not DriveType.Fixed)
        {
            throw new InvalidDataException("Evaluation paths must be explicit canonical absolute paths on fixed local drives.");
        }
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException("Evaluation input cannot cross a reparse path.");
                }
            }
        }
    }

    private static void RequireInventory(string root, IEnumerable<string> names)
    {
        RequirePlainAbsolutePath(root);
        var expected = names.Order(StringComparer.Ordinal).ToArray();
        if (Directory.EnumerateDirectories(root).Any()
            || !Directory.EnumerateFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The selected evaluation root has an incomplete or unknown inventory.");
        }
    }

    private static bool IsDigest([NotNullWhen(true)] string? digest) => digest is { Length: 64 } && digest.All(char.IsAsciiHexDigit);

    internal static string CreateScratchDirectory([CallerFilePath] string source = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "..", ".scratch", "offline-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        return root;
    }

    internal IAsyncDisposable TransferToSession()
    {
        if (sessionOwned) { throw new InvalidOperationException("The evaluation assets already have a session owner."); }
        sessionOwned = true;
        return new SessionAssets(this);
    }

    public ValueTask DisposeAsync() => sessionOwned ? ValueTask.CompletedTask : ReleaseOnce();
    private ValueTask ReleaseOnce() => new(release ??= Release());
    private async Task Release()
    {
        foreach (var stream in corpus.Values) { await stream.DisposeAsync(); }
        await assets.DisposeAsync();
        roots.Dispose();
    }

    internal sealed record Manifest(int Version, string AssetsRoot, string NativePath, string CorpusRoot, ImmutableArray<ReplayCase> Cases);
    internal sealed record ReplayCase(string Id, string File, long Bytes, string Sha256, int ChunkSamples,
        int FirstFrameMinimum, int FirstFrameMaximum, int LastFrameMinimum, int LastFrameMaximum,
        long CommandEndMinimum, long CommandEndMaximum);

    internal sealed class NativeAssets(HandsFreeNativeProfile profile, HandsFreeAssetLease lease,
        SpeechComponentFileBinding roots) : IAsyncDisposable
    {
        internal HandsFreeNativeProfile Profile { get; } = profile;
        public async ValueTask DisposeAsync()
        {
            try { await lease.DisposeAsync(); }
            finally { roots.Dispose(); }
        }
    }

    private sealed class SessionAssets(HandsFreeOfflineFixture owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => owner.ReleaseOnce();
    }

    internal sealed class FileMicrophone : IHandsFreeMicrophone
    {
        private readonly FileStream stream;
        private readonly byte[] bytes;
        private bool started;
        private bool disposed;
        internal FileMicrophone(FileStream stream, int chunkSamples)
        {
            this.stream = stream;
            bytes = new byte[checked(chunkSamples * 2)];
        }
        public event HandsFreeSamplesAvailable? Samples;
        public event EventHandler? Failed;
        internal long PositionSamples => stream.Position / 2;
        public void Start()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) { throw new InvalidOperationException("File replay starts only once."); }
            stream.Position = 0;
            started = true;
        }
        internal async Task<bool> Pump(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started) { throw new InvalidOperationException("File replay was not started."); }
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(bytes.Length, stream.Length - stream.Position);
            if (count == 0) { return false; }
            try
            {
                await stream.ReadExactlyAsync(bytes.AsMemory(0, count), cancellationToken);
                Samples?.Invoke(MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, count)));
                return true;
            }
            catch (IOException)
            {
                Failed?.Invoke(this, EventArgs.Empty);
                throw;
            }
            finally { Array.Clear(bytes); }
        }
        public async ValueTask DisposeAsync()
        {
            disposed = true;
            Samples = null;
            Failed = null;
            Array.Clear(bytes);
            await stream.DisposeAsync();
        }
    }
}
