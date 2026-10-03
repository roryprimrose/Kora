using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Windows.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class KokoroTextToSpeechProviderTests : IDisposable
{
    private static readonly Uri ModelUri = new("https://example.invalid/model.onnx");
    private static readonly Uri VoicesUri = new("https://example.invalid/voices.zip");
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

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
        string? modelHash = null) =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            client,
            NullLogger<KokoroTextToSpeechProvider>.Instance,
            ModelUri,
            model.LongLength,
            modelHash ?? GetSha256(model),
            VoicesUri,
            voices.LongLength,
            GetSha256(voices),
            prepareAfterInstall: false);

    private static HttpClient CreateClient(byte[] model, byte[] voices) =>
        new(new AssetMessageHandler(
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [ModelUri.AbsolutePath] = model,
                [VoicesUri.AbsolutePath] = voices,
            }));

    private static byte[] CreateVoiceArchive(string entryName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
#pragma warning disable VSTHRD103 // In-memory test archive creation is synchronous.
            using var entryStream = entry.Open();
#pragma warning restore VSTHRD103
            entryStream.Write([1, 2, 3]);
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
