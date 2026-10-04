using System.Net.Http.Json;
using System.Text.Json;

using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

/// <summary>
/// Performs the approved Ollama installation action. Call InstallAsync only after the
/// user has explicitly consented to installing Ollama and downloading the model.
/// </summary>
public sealed class WindowsOllamaSetupService : ILocalModelSetup
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

    public WindowsOllamaSetupService()
        : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(30),
        }, new OllamaProcessRunner())
    {
    }

    internal WindowsOllamaSetupService(HttpClient client, IOllamaProcessRunner processes)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    /// <summary>
    /// After caller consent, installs Ollama for the current Windows user if no local
    /// runtime is available, downloads the pinned model if absent, and proves inference works.
    /// Throws on failure or cancellation; it never reports success without verification.
    /// </summary>
    public async Task InstallAsync(
        IProgress<LocalModelSetupProgress> progress,
        CancellationToken cancellationToken)
    {
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
            var executable = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe");
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

                if (!await IsRuntimeAvailableAsync(cancellationToken) && processes.HasInstalledRuntime(executable))
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

            await WaitForRuntimeAsync(cancellationToken);
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

    private async Task<bool> IsRuntimeAvailableAsync(CancellationToken cancellationToken)
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

            return true;
        }
        catch (HttpRequestException exception) when (exception.InnerException is System.Net.Sockets.SocketException
            { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
        {
            return false;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The local Ollama endpoint timed out; refusing to install over an unknown runtime.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The local endpoint returned invalid Ollama metadata.", exception);
        }
    }

    private async Task WaitForRuntimeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsRuntimeAvailableAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new InvalidOperationException("Ollama did not become available on 127.0.0.1:11434 after installation/startup.");
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
                    && string.Equals(digest.GetString(), ModelDigest, StringComparison.OrdinalIgnoreCase))
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

                progress.Report(new(
                    $"Downloading {Model}: {downloaded:N0} of {total:N0} bytes.",
                    (int)Math.Floor(100d * downloaded / total)));
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
