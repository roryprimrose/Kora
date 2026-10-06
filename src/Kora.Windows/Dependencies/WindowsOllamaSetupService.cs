using System.Net.Http.Json;
using System.Text.Json;

using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

/// <summary>
/// Performs the approved Ollama installation action. Call InstallAsync only after the
/// user has explicitly consented to installing Ollama and downloading the model.
/// </summary>
public sealed class WindowsOllamaSetupService : ILocalModelSetup, IDisposable
{
    public const string PackageId = "Ollama.Ollama";
    public const string PackageVersion = "0.35.1";
    public const string Model = "qwen3:1.7b";
    public const string ModelDigest = "sha256:8F68893C685C3DDFF2AA3FFFCE2AA60A30BB2DA65CA488B61FFF134A4D1730E7";
    public const string ModelDownloadSize = "approximately 1.36 GB";

    private static readonly Uri VersionUri = new("http://127.0.0.1:11434/api/version");
    private static readonly Uri TagsUri = new("http://127.0.0.1:11434/api/tags");
    private static readonly Uri PullUri = new("http://127.0.0.1:11434/api/pull");
    private static readonly Uri GenerateUri = new("http://127.0.0.1:11434/api/generate");
    private readonly HttpClient client;
    private readonly IOllamaProcessRunner processes;
    private readonly bool ownsClient;
    private bool disposed;

    public WindowsOllamaSetupService()
        : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(30),
        }, new OllamaProcessRunner())
    {
        ownsClient = true;
    }

    public WindowsOllamaSetupService(HttpClient client)
        : this(client, new OllamaProcessRunner())
    {
    }

    internal WindowsOllamaSetupService(HttpClient client, IOllamaProcessRunner processes)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        processes.StopOwnedServer();
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    public async Task<OllamaInstallationStatus> InspectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var version = await GetRuntimeVersionAsync(timeout.Token);
            if (version is null)
            {
                return processes.HasInstalledRuntime(InstalledExecutablePath)
                    ? new(OllamaInstallationState.NotRunning,
                        "A per-user Ollama executable was detected, but no endpoint is running. Model availability is unknown; no service was started.")
                    : new(OllamaInstallationState.Missing,
                        "No local endpoint or supported per-user Ollama installation was found.");
            }

            return await HasPinnedModelAsync(timeout.Token)
                ? new(OllamaInstallationState.ModelDetected,
                    $"Ollama {version} reports the pinned {Model} digest. Inference has not been tested; verification requires approval.")
                : new(OllamaInstallationState.ModelMissing,
                    $"Ollama {version} is responding, but {Model} is missing. Reuse the runtime; model download is {ModelDownloadSize}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(OllamaInstallationState.Unverifiable,
                "Local metadata discovery timed out. No runtime or model was changed.");
        }
        catch (InvalidOperationException exception) when (exception.InnerException is OperationCanceledException)
        {
            return new(OllamaInstallationState.Unverifiable, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new(OllamaInstallationState.Incompatible, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or IOException or UnauthorizedAccessException)
        {
            return new(OllamaInstallationState.Unverifiable,
                $"Local Ollama metadata could not be verified: {exception.Message}");
        }
    }

    private static string InstalledExecutablePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Ollama", "ollama.exe");

    /// <summary>
    /// After caller consent, installs Ollama for the current Windows user if no local
    /// runtime is available, downloads the pinned model if absent, and proves inference works.
    /// Throws on failure or cancellation; it never reports success without verification.
    /// </summary>
    public async Task InstallAsync(
        IProgress<LocalModelSetupProgress> progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();
        var startedServer = 0;
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (Volatile.Read(ref startedServer) != 0)
            {
                processes.StopOwnedServer();
            }
        });

        progress.Report(new("Checking Ollama on 127.0.0.1:11434."));
        if (!await IsRuntimeAvailableAsync(cancellationToken))
        {
            var executable = InstalledExecutablePath;
            if (processes.HasInstalledRuntime(executable))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new("Starting the existing per-user Ollama installation."));
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref startedServer, 1);
                processes.StartServer(executable);
                if (cancellationToken.IsCancellationRequested)
                {
                    processes.StopOwnedServer();
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new($"Installing {PackageId} {PackageVersion} for the current user via winget."));
                var exitCode = await processes.InstallWingetAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"winget Ollama installation failed with exit code {exitCode}.");
                }

                progress.Report(new("Waiting for the newly installed Ollama runtime."));
                if (!await WaitForRuntimeAsync(attempts: 10, cancellationToken)
                    && processes.HasInstalledRuntime(executable))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.Report(new("Starting the newly installed Ollama runtime."));
                    cancellationToken.ThrowIfCancellationRequested();
                    Interlocked.Exchange(ref startedServer, 1);
                    processes.StartServer(executable);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        processes.StopOwnedServer();
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            if (!await WaitForRuntimeAsync(attempts: 30, cancellationToken))
            {
                throw new InvalidOperationException(
                    "Ollama did not become available on 127.0.0.1:11434 after installation/startup.");
            }
        }
        else
        {
            progress.Report(new("Reusing the healthy local Ollama runtime."));
        }

        if (!await HasPinnedModelAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modelDirectory = Environment.GetEnvironmentVariable("OLLAMA_MODELS")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".ollama", "models");
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(modelDirectory))
                ?? throw new InvalidOperationException("Ollama model storage has no filesystem root.");
            if (new DriveInfo(driveRoot).AvailableFreeSpace < 2_000_000_000)
            {
                throw new IOException(
                    $"At least 2 GB free is required on {driveRoot} before downloading {Model}.");
            }

            progress.Report(new($"Downloading {Model} ({ModelDownloadSize}); verifying {ModelDigest}."));
            await PullModelAsync(progress, cancellationToken);
            progress.Report(new($"Verifying the downloaded {Model} digest."));
            if (!await HasPinnedModelAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Ollama did not report the required digest {ModelDigest} for {Model} after download.");
            }
        }

        progress.Report(new($"Testing inference with {Model}."));
        await VerifyGenerationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new($"Ollama and {Model} verified successfully."));
    }

    private async Task<bool> IsRuntimeAvailableAsync(
        CancellationToken cancellationToken,
        bool tolerateStartupTimeout = false)
        => await GetRuntimeVersionAsync(cancellationToken, tolerateStartupTimeout) is not null;

    private async Task<string?> GetRuntimeVersionAsync(
        CancellationToken cancellationToken,
        bool tolerateStartupTimeout = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await client.GetAsync(VersionUri, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            if (!document.RootElement.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(version.GetString()))
            {
                throw new InvalidOperationException("The local endpoint is not a compatible Ollama runtime.");
            }

            return version.GetString();
        }
        catch (HttpRequestException exception) when (exception.InnerException is System.Net.Sockets.SocketException
        { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
        {
            return null;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (tolerateStartupTimeout)
            {
                return null;
            }

            throw new InvalidOperationException(
                "The local Ollama endpoint timed out; refusing to install over an unknown runtime.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The local endpoint returned invalid Ollama metadata.", exception);
        }
    }

    private async Task<bool> WaitForRuntimeAsync(
        int attempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsRuntimeAvailableAsync(cancellationToken, tolerateStartupTimeout: true))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return false;
    }

    private async Task<bool> HasPinnedModelAsync(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(TagsUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Ollama returned an invalid model catalogue.");
        }

        foreach (var model in models.EnumerateArray())
        {
            if (model.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && string.Equals(name.GetString(), Model, StringComparison.Ordinal))
            {
                if (model.TryGetProperty("digest", out var digest)
                    && digest.ValueKind == JsonValueKind.String
                    && OllamaModelIdentity.HasPinnedDigest(digest.GetString()))
                {
                    return true;
                }

                throw new InvalidOperationException(
                    $"The installed {Model} has a different or missing digest; refusing to replace it.");
            }
        }

        return false;
    }

    private async Task PullModelAsync(
        IProgress<LocalModelSetupProgress> progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PullUri)
        {
            Content = JsonContent.Create(new { name = Model, stream = true }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var completed = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"Ollama model download failed: {error.GetString()}");
            }

            if (document.RootElement.TryGetProperty("total", out var totalValue)
                && document.RootElement.TryGetProperty("completed", out var completedValue))
            {
                if (totalValue.ValueKind != JsonValueKind.Number
                    || completedValue.ValueKind != JsonValueKind.Number
                    || !totalValue.TryGetInt64(out var total)
                    || !completedValue.TryGetInt64(out var downloaded)
                    || total <= 0
                    || downloaded < 0
                    || downloaded > total)
                {
                    throw new InvalidDataException("Ollama reported invalid model download progress.");
                }

                if (downloaded == total)
                {
                    progress.Report(new(
                        $"Downloaded {FormatBytes(total)} for {Model}. Finalizing the model locally."));
                }
                else
                {
                    var percentage = (int)Math.Floor(100d * downloaded / total);
                    progress.Report(new(
                        $"Downloading {Model}: {FormatBytes(downloaded)} of {FormatBytes(total)} ({percentage}%).",
                        percentage));
                }
            }

            if (document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "success", StringComparison.Ordinal))
            {
                completed = true;
            }
        }

        if (!completed)
        {
            throw new InvalidOperationException("Ollama model download ended without a success status.");
        }
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1_000_000_000
            ? $"{bytes / 1_000_000_000d:0.00} GB"
            : $"{bytes / 1_000_000d:0.0} MB";

    private async Task VerifyGenerationAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GenerateUri)
        {
            Content = JsonContent.Create(new { model = Model, prompt = "Respond with OK.", stream = false }),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"Ollama inference failed: {error.GetString()}");
        }

        if (!root.TryGetProperty("model", out var model)
            || model.ValueKind != JsonValueKind.String
            || !string.Equals(model.GetString(), Model, StringComparison.Ordinal)
            || !root.TryGetProperty("done", out var done)
            || done.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("response", out var text)
            || text.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(text.GetString()))
        {
            throw new InvalidOperationException("Ollama did not return a completed, nonempty response from the pinned model.");
        }
    }
}