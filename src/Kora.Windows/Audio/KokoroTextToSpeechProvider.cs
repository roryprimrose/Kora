using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Windows.Diagnostics;

using KokoroSharp;

using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace Kora.Windows.Audio;

public sealed class KokoroTextToSpeechProvider : IDisposable
{
    internal const long ModelSizeBytes = 163_636_560;
    internal const long VoicesSizeBytes = 65_813_438;
    internal const long DownloadSizeBytes = ModelSizeBytes + VoicesSizeBytes;
    internal const string ModelSha256 =
        "027A25B14AEF7D3AE57FD09301EBEFBEC868E79D55213D07E4F3AF442F5BA352";
    internal const string VoicesSha256 =
        "313D823EE9EA8828C14B28042B933BBC03955D402AFB785CBE0B877827D3EA1D";
    internal static readonly Uri ModelUri = new(
        "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro-fp16.onnx");
    internal static readonly Uri VoicesUri = new(
        "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/voices.zip");

    private const string ModelVersion = "v2.0.0";
    private const string ModelFileName = "kokoro-fp16.onnx";
    private const string HashFileName = "kokoro-fp16.sha256";
    private const string TemporaryModelFileName = "kokoro-fp16.download";
    private const string TemporaryHashFileName = "kokoro-fp16.sha256.tmp";
    private const string VoicesDirectoryName = "voices";
    private const string VoicesHashFileName = "voices.sha256";
    private const string TemporaryVoicesFileName = "voices.download.zip";
    private const string TemporaryVoicesHashFileName = "voices.sha256.tmp";
    private const string TemporaryVoicesDirectoryName = "voices.installing";
    private static readonly Dictionary<char, string> VoiceCultures =
        new Dictionary<char, string>
        {
            ['a'] = "en-US",
            ['b'] = "en-GB",
            ['e'] = "es-ES",
            ['f'] = "fr-FR",
            ['h'] = "hi-IN",
            ['i'] = "it-IT",
            ['j'] = "ja-JP",
            ['p'] = "pt-BR",
            ['z'] = "zh-CN",
        };

    private readonly HttpClient httpClient;
    private readonly ILogger<KokoroTextToSpeechProvider> logger;
    private readonly SemaphoreSlim modelLock = new(1, 1);
    private readonly Uri modelUri;
    private readonly Uri voicesUri;
    private readonly long expectedModelSize;
    private readonly long expectedVoicesSize;
    private readonly string expectedModelSha256;
    private readonly string expectedVoicesSha256;
    private readonly string providerDirectory;
    private readonly string modelPath;
    private readonly string hashPath;
    private readonly string voiceDirectory;
    private readonly string voicesHashPath;
    private readonly bool prepareAfterInstall;
    private KokoroWavSynthesizer? synthesizer;
    private bool disposed;

    public KokoroTextToSpeechProvider(
        IApplicationDataPaths paths,
        HttpClient httpClient,
        ILogger<KokoroTextToSpeechProvider> logger)
        : this(
            paths,
            httpClient,
            logger,
            ModelUri,
            ModelSizeBytes,
            ModelSha256,
            VoicesUri,
            VoicesSizeBytes,
            VoicesSha256,
            prepareAfterInstall: true)
    {
    }

    internal KokoroTextToSpeechProvider(
        IApplicationDataPaths paths,
        HttpClient httpClient,
        ILogger<KokoroTextToSpeechProvider> logger,
        Uri modelUri,
        long expectedModelSize,
        string expectedModelSha256,
        Uri voicesUri,
        long expectedVoicesSize,
        string expectedVoicesSha256,
        bool prepareAfterInstall)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(modelUri);
        ArgumentNullException.ThrowIfNull(voicesUri);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedModelSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVoicesSize);
        ValidateSha256(expectedModelSha256, nameof(expectedModelSha256));
        ValidateSha256(expectedVoicesSha256, nameof(expectedVoicesSha256));

        this.httpClient = httpClient;
        this.logger = logger;
        this.modelUri = modelUri;
        this.voicesUri = voicesUri;
        this.expectedModelSize = expectedModelSize;
        this.expectedVoicesSize = expectedVoicesSize;
        this.expectedModelSha256 = expectedModelSha256;
        this.expectedVoicesSha256 = expectedVoicesSha256;
        this.prepareAfterInstall = prepareAfterInstall;
        providerDirectory = Path.Combine(
            paths.LocalRoot,
            "Speech",
            "Kokoro",
            ModelVersion);
        modelPath = Path.Combine(providerDirectory, ModelFileName);
        hashPath = Path.Combine(providerDirectory, HashFileName);
        voiceDirectory = Path.Combine(providerDirectory, VoicesDirectoryName);
        voicesHashPath = Path.Combine(providerDirectory, VoicesHashFileName);
    }

    public bool IsInstalled =>
        File.Exists(modelPath)
        && new FileInfo(modelPath).Length == expectedModelSize
        && File.Exists(hashPath)
        && string.Equals(
            File.ReadAllText(hashPath).Trim(),
            expectedModelSha256,
            StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(voiceDirectory)
        && Directory.EnumerateFiles(
            voiceDirectory,
            "*.npy",
            SearchOption.AllDirectories).Any()
        && File.Exists(voicesHashPath)
        && string.Equals(
            File.ReadAllText(voicesHashPath).Trim(),
            expectedVoicesSha256,
            StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<SpeechVoice> GetVoices()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsInstalled)
        {
            return [];
        }

        if (!Directory.Exists(voiceDirectory))
        {
            throw new InvalidOperationException(
                "Kora's Kokoro voice assets are missing from the application installation.");
        }

        return Directory.GetFiles(voiceDirectory, "*.npy", SearchOption.AllDirectories)
            .Select(CreateSpeechVoice)
            .Where(voice => voice is not null)
            .Cast<SpeechVoice>()
            .OrderBy(voice => voice.Culture, StringComparer.Ordinal)
            .ThenBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task InstallAsync(
        IProgress<SpeechProviderInstallProgress> progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(progress);
        if (IsInstalled)
        {
            if (prepareAfterInstall)
            {
                await EnsureLoadedAsync(cancellationToken);
            }

            return;
        }

        Directory.CreateDirectory(providerDirectory);
        var temporaryModelPath = Path.Combine(
            providerDirectory,
            TemporaryModelFileName);
        var temporaryHashPath = Path.Combine(
            providerDirectory,
            TemporaryHashFileName);
        var temporaryVoicesPath = Path.Combine(
            providerDirectory,
            TemporaryVoicesFileName);
        var temporaryVoicesHashPath = Path.Combine(
            providerDirectory,
            TemporaryVoicesHashFileName);
        var temporaryVoicesDirectory = Path.Combine(
            providerDirectory,
            TemporaryVoicesDirectoryName);
        try
        {
            await DownloadAndVerifyAsync(
                modelUri,
                expectedModelSize,
                expectedModelSha256,
                temporaryModelPath,
                progress,
                completedBytes: 0,
                totalBytes: expectedModelSize + expectedVoicesSize,
                cancellationToken);
            await DownloadAndVerifyAsync(
                voicesUri,
                expectedVoicesSize,
                expectedVoicesSha256,
                temporaryVoicesPath,
                progress,
                completedBytes: expectedModelSize,
                totalBytes: expectedModelSize + expectedVoicesSize,
                cancellationToken);
            progress.Report(new SpeechProviderInstallProgress(
                SpeechProviderInstallStage.Extracting,
                expectedModelSize + expectedVoicesSize,
                expectedModelSize + expectedVoicesSize));
            DeleteDirectoryIfExists(temporaryVoicesDirectory);
            await ExtractVoicesAsync(
                temporaryVoicesPath,
                temporaryVoicesDirectory,
                cancellationToken);
            File.Move(temporaryModelPath, modelPath, overwrite: true);
            DeleteDirectoryIfExists(voiceDirectory);
            Directory.Move(temporaryVoicesDirectory, voiceDirectory);
            await File.WriteAllTextAsync(
                temporaryHashPath,
                expectedModelSha256,
                cancellationToken);
            File.Move(temporaryHashPath, hashPath, overwrite: true);
            await File.WriteAllTextAsync(
                temporaryVoicesHashPath,
                expectedVoicesSha256,
                cancellationToken);
            File.Move(
                temporaryVoicesHashPath,
                voicesHashPath,
                overwrite: true);
            progress.Report(new SpeechProviderInstallProgress(
                SpeechProviderInstallStage.Preparing,
                expectedModelSize + expectedVoicesSize,
                expectedModelSize + expectedVoicesSize));
            if (prepareAfterInstall)
            {
                await EnsureLoadedAsync(cancellationToken);
            }

            WindowsLog.Information(logger, "Kokoro speech provider installed");
        }
        finally
        {
            DeleteIfExists(temporaryModelPath);
            DeleteIfExists(temporaryHashPath);
            DeleteIfExists(temporaryVoicesPath);
            DeleteIfExists(temporaryVoicesHashPath);
            DeleteDirectoryIfExists(temporaryVoicesDirectory);
        }
    }

    public async Task<KokoroAudio> SynthesizeAsync(
        string text,
        string voiceId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);
        if (!IsInstalled)
        {
            throw new InvalidOperationException(
                "The Kokoro speech provider is not installed.");
        }

        await EnsureLoadedAsync(cancellationToken);
        var voice = KokoroVoiceManager.GetVoice(voiceId);
        byte[] samples;
        try
        {
            samples = await synthesizer!.SynthesizeAsync(text, voice);
        }
        catch (OnnxRuntimeException exception)
        {
            throw new InvalidOperationException(
                "The Kokoro neural model could not synthesize speech.",
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new KokoroAudio(samples, 24_000, 16, 1);
    }

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await modelLock.WaitAsync(cancellationToken);
        try
        {
            synthesizer?.Dispose();
            synthesizer = null;
            DeleteIfExists(modelPath);
            DeleteIfExists(hashPath);
            DeleteIfExists(voicesHashPath);
            DeleteIfExists(Path.Combine(providerDirectory, TemporaryModelFileName));
            DeleteIfExists(Path.Combine(providerDirectory, TemporaryHashFileName));
            DeleteIfExists(Path.Combine(providerDirectory, TemporaryVoicesFileName));
            DeleteIfExists(Path.Combine(providerDirectory, TemporaryVoicesHashFileName));
            DeleteDirectoryIfExists(voiceDirectory);
            DeleteDirectoryIfExists(Path.Combine(
                providerDirectory,
                TemporaryVoicesDirectoryName));
            if (Directory.Exists(providerDirectory)
                && !Directory.EnumerateFileSystemEntries(providerDirectory).Any())
            {
                Directory.Delete(providerDirectory);
            }

            WindowsLog.Information(logger, "Kokoro speech provider removed");
        }
        finally
        {
            modelLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        synthesizer?.Dispose();
        synthesizer = null;
        modelLock.Dispose();
    }

    private async Task DownloadAndVerifyAsync(
        Uri uri,
        long expectedSize,
        string expectedSha256,
        string temporaryPath,
        IProgress<SpeechProviderInstallProgress> progress,
        long completedBytes,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength != expectedSize)
        {
            throw new InvalidDataException(
                $"A Kokoro asset reported {contentLength:N0} bytes; {expectedSize:N0} were expected.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        await using var destination = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        long bytesReceived = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       cancellationToken)) > 0)
            {
                bytesReceived += bytesRead;
                if (bytesReceived > expectedSize)
                {
                    throw new InvalidDataException(
                        "A Kokoro asset download exceeded its expected size.");
                }

                hash.AppendData(buffer, 0, bytesRead);
                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
                progress.Report(new SpeechProviderInstallProgress(
                    SpeechProviderInstallStage.Downloading,
                    completedBytes + bytesReceived,
                    totalBytes));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await destination.FlushAsync(cancellationToken);
        if (bytesReceived != expectedSize)
        {
            throw new InvalidDataException(
                $"A Kokoro asset contained {bytesReceived:N0} bytes; {expectedSize:N0} were expected.");
        }

        progress.Report(new SpeechProviderInstallProgress(
            SpeechProviderInstallStage.Verifying,
            completedBytes + bytesReceived,
            totalBytes));
        var actualHash = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(
                actualHash,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The Kokoro asset '{Path.GetFileName(uri.LocalPath)}' failed SHA-256 validation. Expected {expectedSha256}; received {actualHash}.");
        }
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException(
                "A SHA-256 digest must contain exactly 64 hexadecimal characters.",
                parameterName);
        }
    }

    private static async Task ExtractVoicesAsync(
        string archivePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);
        var destinationRoot = Path.GetFullPath(destinationPath)
            + Path.DirectorySeparatorChar;
        using var archive = await Task.Run(
            () => ZipFile.OpenRead(archivePath),
            cancellationToken);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryPath = Path.GetFullPath(
                Path.Combine(destinationPath, entry.FullName));
            if (!entryPath.StartsWith(
                    destinationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The Kokoro voice archive contains an unsafe path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(entryPath);
                continue;
            }

            var entryDirectory = Path.GetDirectoryName(entryPath)
                ?? throw new InvalidDataException(
                    "The Kokoro voice archive contains an invalid path.");
            Directory.CreateDirectory(entryDirectory);
            await using var source = await Task.Run(
                entry.Open,
                cancellationToken);
            await using var destination = new FileStream(
                entryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        await modelLock.WaitAsync(cancellationToken);
        try
        {
            if (synthesizer is null)
            {
                KokoroVoiceManager.LoadVoicesFromPath(voiceDirectory);
                try
                {
                    synthesizer = await Task.Run(
                        () => new KokoroWavSynthesizer(modelPath),
                        cancellationToken);
                }
                catch (OnnxRuntimeException exception)
                {
                    throw new InvalidOperationException(
                        "The Kokoro neural model could not be loaded.",
                        exception);
                }

                WindowsLog.Information(logger, "Kokoro neural model loaded");
            }
        }
        finally
        {
            modelLock.Release();
        }
    }

    private static SpeechVoice? CreateSpeechVoice(string path)
    {
        var id = Path.GetFileNameWithoutExtension(path);
        if (id.Length < 4
            || id[2] != '_'
            || !VoiceCultures.TryGetValue(id[0], out var culture))
        {
            return null;
        }

        var gender = id[1] switch
        {
            'f' => SpeechVoiceGender.Female,
            'm' => SpeechVoiceGender.Male,
            _ => SpeechVoiceGender.Unknown,
        };
        var name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            id[3..].Replace('_', ' '));
        return new SpeechVoice(id, name, culture, gender)
        {
            ProviderId = SpeechProviderIds.Kokoro,
        };
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
