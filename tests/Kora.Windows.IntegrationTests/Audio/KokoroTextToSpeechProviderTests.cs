using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Windows.Audio;

using KokoroSharp.Core;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed partial class KokoroTextToSpeechProviderTests : IDisposable
{
    private static readonly Uri ModelUri = new("https://example.invalid/model.onnx");
    private static readonly Uri VoicesUri = new("https://example.invalid/voices.zip");
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Presence_inspection_does_not_create_directories_or_acquire_assets()
    {
        byte[] model = [1, 2, 3, 4];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices);
        provider.IsInstalled.Should().BeFalse();
        provider.HasLocalAssets.Should().BeFalse();
        Directory.Exists(root).Should().BeFalse();
        var partial = Path.Combine(root, "Speech", "Kokoro", "v2.0.0", "partial.download");
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllTextAsync(partial, "partial", TestContext.Current.CancellationToken);
        provider.HasLocalAssets.Should().BeTrue();
        provider.IsInstalled.Should().BeFalse();
    }

    [Fact]
    public async Task InstallAsync_validates_activates_and_removes_downloaded_assets()
    {
        byte[] model = [1, 2, 3, 4];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices);
        var progress = new RecordingProgress<SpeechProviderInstallProgress>();

        await provider.InstallAsync(
            progress,
            TestContext.Current.CancellationToken);

        provider.IsInstalled.Should().BeTrue();
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            provider.GetVoices().Should().ContainSingle()
                .Which.Should().BeEquivalentTo(
                    new SpeechVoice(
                        "af_heart",
                        "Heart",
                        "en-US",
                        SpeechVoiceGender.Female)
                    {
                        ProviderId = SpeechProviderIds.Kokoro,
                    });
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
        progress.Values.Select(item => item.Stage).Should().Contain(
        [
            SpeechProviderInstallStage.Downloading,
            SpeechProviderInstallStage.Verifying,
            SpeechProviderInstallStage.Extracting,
            SpeechProviderInstallStage.Preparing,
        ]);
        progress.Values.Last().Percentage.Should().Be(100);

        await provider.InstallAsync(
            progress,
            TestContext.Current.CancellationToken);
        await provider.RemoveAsync(TestContext.Current.CancellationToken);

        provider.IsInstalled.Should().BeFalse();
        provider.GetVoices().Should().BeEmpty();
    }

    [Fact]
    public async Task Voice_options_follow_profile_language_changes_without_changing_installed_assets()
    {
        byte[] model = [1, 2, 3, 4];
        var voices = CreateVoiceArchive("voices/af_heart.npy", "voices/bf_emma.npy", "voices/ff_siwis.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices);
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);

        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-AU");
            provider.GetVoices().Select(voice => voice.Id).Should().Equal("bf_emma", "af_heart");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-CA");
            provider.GetVoices().Should().ContainSingle().Which.Id.Should().Be("ff_siwis");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            provider.GetVoices().Should().BeEmpty();
            provider.IsInstalled.Should().BeTrue();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            provider.GetVoices().Should().HaveCount(2);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Theory]
    [InlineData("en-AU", "af_heart", "bf_emma")]
    [InlineData("en", "af_heart", "bf_emma")]
    [InlineData("es-MX", "ef_dora")]
    [InlineData("fr-CA", "ff_siwis")]
    [InlineData("hi", "hf_alpha")]
    [InlineData("it-CH", "if_sara")]
    [InlineData("ja-JP", "jf_alpha")]
    [InlineData("pt-PT", "pf_dora")]
    [InlineData("zh-TW", "zf_xiaobei")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task Voice_options_match_only_the_profile_UI_language(string profileCulture, params string[] expectedVoiceIds)
    {
        byte[] model = [1, 2, 3, 4];
        var voices = CreateVoiceArchive(
            "voices/af_heart.npy", "voices/bf_emma.npy", "voices/ef_dora.npy",
            "voices/ff_siwis.npy", "voices/hf_alpha.npy", "voices/if_sara.npy",
            "voices/jf_alpha.npy", "voices/pf_dora.npy", "voices/zf_xiaobei.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices);
        await provider.InstallAsync(new RecordingProgress<SpeechProviderInstallProgress>(), TestContext.Current.CancellationToken);

        var previousUICulture = CultureInfo.CurrentUICulture;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(profileCulture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(profileCulture.StartsWith("en", StringComparison.Ordinal) ? "fr-FR" : "en-US");
            provider.GetVoices().Select(voice => voice.Id).Should().BeEquivalentTo(expectedVoiceIds);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUICulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task InstallAsync_rejects_an_asset_with_the_wrong_hash()
    {
        byte[] model = [1, 2, 3, 4];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(
            client,
            model,
            voices,
            modelHash: new string('0', 64));

        var action = () => provider.InstallAsync(
            new RecordingProgress<SpeechProviderInstallProgress>(),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*SHA-256*");
        provider.IsInstalled.Should().BeFalse();
        Directory.GetFiles(root, "*.download", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void Constructor_rejects_a_malformed_SHA256_digest(string modelHash)
    {
        byte[] model = [1, 2, 3, 4];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);

        var action = () => CreateProvider(
            client,
            model,
            voices,
            modelHash);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*64 hexadecimal characters*");
    }

    [Fact]
    public async Task InstallAsync_rejects_unsafe_voice_archive_paths()
    {
        byte[] model = [1, 2, 3, 4];
        var voices = CreateVoiceArchive("../outside.npy");
        using var client = CreateClient(model, voices);
        using var provider = CreateProvider(client, model, voices);

        var action = () => provider.InstallAsync(
            new RecordingProgress<SpeechProviderInstallProgress>(),
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*unsafe path*");
        File.Exists(Path.Combine(root, "Speech", "Kokoro", "outside.npy"))
            .Should().BeFalse();
        provider.IsInstalled.Should().BeFalse();
    }

    [Fact]
    public async Task Disposed_provider_rejects_operations()
    {
        byte[] model = [1];
        var voices = CreateVoiceArchive("voices/af_heart.npy");
        using var client = CreateClient(model, voices);
        var provider = CreateProvider(client, model, voices);

        provider.Dispose();
        provider.Dispose();

        var getVoices = provider.GetVoices;
        getVoices.Should().Throw<ObjectDisposedException>();
        var install = () => provider.InstallAsync(
            new RecordingProgress<SpeechProviderInstallProgress>(),
            TestContext.Current.CancellationToken);
        await install.Should().ThrowAsync<ObjectDisposedException>();
        var remove = () => provider.RemoveAsync(
            TestContext.Current.CancellationToken);
        await remove.Should().ThrowAsync<ObjectDisposedException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private KokoroTextToSpeechProvider CreateProvider(
        HttpClient client,
        byte[] model,
        byte[] voices,
        string? modelHash = null,
        Func<string, IKokoroSynthesizer>? createSynthesizer = null,
        Func<string, KokoroVoice>? loadVoice = null,
        ILogger<KokoroTextToSpeechProvider>? logger = null,
        bool prepareAfterInstall = false,
        Action<KokoroVoice>? prepareLanguage = null) =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            client,
            logger ?? NullLogger<KokoroTextToSpeechProvider>.Instance,
            ModelUri,
            model.LongLength,
            modelHash ?? GetSha256(model),
            VoicesUri,
            voices.LongLength,
            GetSha256(voices),
            prepareAfterInstall,
            createSynthesizer,
            loadVoice,
            prepareLanguage);

    private static HttpClient CreateClient(byte[] model, byte[] voices) =>
        new(new AssetMessageHandler(
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [ModelUri.AbsolutePath] = model,
                [VoicesUri.AbsolutePath] = voices,
            }));

    private static byte[] CreateVoiceArchive(params string[] entryNames)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var entryName in entryNames)
            {
                var entry = archive.CreateEntry(entryName);
#pragma warning disable VSTHRD103 // In-memory test archive creation is synchronous.
                using var entryStream = entry.Open();
#pragma warning restore VSTHRD103
                entryStream.Write([1, 2, 3]);
            }
        }

        return stream.ToArray();
    }

    private static string GetSha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value));

    private sealed record TestPaths(
        string LocalRoot,
        string RoamingRoot) : IApplicationDataPaths;

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }

    private sealed class AssetMessageHandler(
        IReadOnlyDictionary<string, byte[]> assets) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.AbsolutePath
                ?? throw new InvalidOperationException("The request URI is missing.");
            if (!assets.TryGetValue(path, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
        }
    }
}
