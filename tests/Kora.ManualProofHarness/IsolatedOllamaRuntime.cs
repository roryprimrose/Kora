using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kora.ManualProofHarness;

internal sealed class IsolatedOllamaRuntime : IAsyncDisposable
{
    internal const int Port = 11435;
    internal const string Endpoint = "http://127.0.0.1:11435";
    internal const string RuntimeVersion = "0.35.1";

    private const string ArchiveName = "ollama-windows-amd64.zip";
    private const string ArchiveSha256 = "dc50b9ca7f9023c86525012632cd1615b093d0407987444a7f62ecab617e8e93";
    private const long ArchiveBytes = 1_471_094_402;
    private const string ModelDigest = "8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7";
    private readonly Process process;
    private readonly long startedUtcTicks;
    private readonly Task outputPump;
    private readonly Task errorPump;
    private readonly string sharedVersion;
    private readonly ProofAttempt attempt;
    private readonly Action persist;

    private IsolatedOllamaRuntime(
        Process process,
        Task outputPump,
        Task errorPump,
        string sharedVersion,
        ProofAttempt attempt,
        Action persist)
    {
        this.process = process;
        this.outputPump = outputPump;
        this.errorPump = errorPump;
        this.sharedVersion = sharedVersion;
        this.attempt = attempt;
        this.persist = persist;
        startedUtcTicks = process.StartTime.ToUniversalTime().Ticks;
    }

    internal int ProcessId => process.Id;

    internal long StartedUtcTicks => startedUtcTicks;

    internal static async Task<IsolatedOllamaRuntime> StartAsync(
        ProofAttempt attempt,
        Action persist,
        TextWriter output)
    {
        var originalSharedVersion = await GetVersionAsync("http://127.0.0.1:11434", CancellationToken.None);
        attempt.AutomatedEvidence["li03.sharedRuntimeVersionBefore"] = originalSharedVersion;
        persist();

        if (await IsPortOpenAsync(Port, CancellationToken.None))
            throw new InvalidOperationException($"Port {Port} is already in use; isolated ownership is not established.");

        var isolationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kora",
            "ManualProofHarness",
            "isolated-ollama");
        var archive = await FindVerifiedArchiveAsync(isolationRoot);
        attempt.AutomatedEvidence["li03.isolatedArchive"] = archive;
        attempt.AutomatedEvidence["li03.isolatedArchiveSha256"] = ArchiveSha256;
        persist();

        var runRoot = Path.Combine(isolationRoot, "runs", attempt.AttemptId.ToString("N"));
        if (Directory.Exists(runRoot))
            throw new InvalidOperationException($"Refusing to reuse an existing isolated runtime directory: {runRoot}");
        var runtimeRoot = Path.Combine(runRoot, "runtime");
        var modelRoot = Path.Combine(runRoot, "models");
        Directory.CreateDirectory(runtimeRoot);
        Directory.CreateDirectory(modelRoot);

        output.WriteLine("Extracting the verified Ollama 0.35.1 portable runtime...");
        ExtractVerifiedArchive(archive, runtimeRoot);
        await PrepareModelStoreAsync(modelRoot);

        var executable = Path.Combine(runtimeRoot, "ollama.exe");
        if (!File.Exists(executable))
            throw new InvalidDataException("The verified archive did not contain ollama.exe.");

        var logPath = Path.Combine(runRoot, "server.log");
        var errorPath = Path.Combine(runRoot, "server.error.log");
        var start = new ProcessStartInfo(executable, "serve")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = runtimeRoot,
        };
        start.Environment["OLLAMA_HOST"] = $"127.0.0.1:{Port}";
        start.Environment["OLLAMA_MODELS"] = modelRoot;

        var serverProcess = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the isolated Ollama runtime.");
        var standardOutputPump = PumpAsync(serverProcess.StandardOutput, logPath);
        var standardErrorPump = PumpAsync(serverProcess.StandardError, errorPath);
        using var serverClient = CreateClient();
        var runtime = new IsolatedOllamaRuntime(
            serverProcess,
            standardOutputPump,
            standardErrorPump,
            originalSharedVersion,
            attempt,
            persist);
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var version = await WaitForVersionAsync(serverClient, startup.Token);
            if (!string.Equals(version, RuntimeVersion, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"The isolated runtime reported {version}; expected exactly {RuntimeVersion}.");
            if (!string.Equals(
                    await GetVersionAsync("http://127.0.0.1:11434", startup.Token),
                    originalSharedVersion,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("The shared Ollama runtime changed during isolated startup.");

            attempt.AutomatedEvidence["li03.isolatedRuntimeRoot"] = runRoot;
            attempt.AutomatedEvidence["li03.serverPid"] = serverProcess.Id.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            attempt.AutomatedEvidence["li03.serverStartedUtcTicks"] = runtime.StartedUtcTicks.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            attempt.AutomatedEvidence["li03.runtimeVersion"] = version;
            attempt.AutomatedEvidence["li03.endpoint"] = Endpoint;
            attempt.AutomatedEvidence["li03.comparisonRuntime"] = bool.FalseString;
            attempt.AutomatedEvidence["li03.isolatedRuntimeStartedUtc"] = DateTimeOffset.UtcNow.ToString("O");
            persist();
            output.WriteLine(
                $"Started isolated Ollama {version}, PID {serverProcess.Id}, on {Endpoint}; shared runtime remains {originalSharedVersion}.");
            return runtime;
        }
        catch
        {
            await runtime.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Exception? cleanupFailure = null;
        try
        {
            if (!process.HasExited)
            {
                if (process.StartTime.ToUniversalTime().Ticks != startedUtcTicks)
                    throw new InvalidOperationException("The isolated Ollama PID was reused; cleanup stopped.");
                try
                {
                    using var cleanupClient = CreateClient();
                    using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/api/generate")
                    {
                        Content = JsonContent.Create(new { model = "qwen3:1.7b", keep_alive = 0, stream = false }),
                    };
                    using var unloadTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var response = await cleanupClient.SendAsync(request, unloadTimeout.Token);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
                {
                    cleanupFailure = exception;
                }

                process.Kill(entireProcessTree: true);
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync(stopTimeout.Token);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException
            or OperationCanceledException or Win32Exception)
        {
            cleanupFailure = exception;
        }
        finally
        {
            await Task.WhenAll(outputPump, errorPump);
            process.Dispose();
        }

        if (await IsPortOpenAsync(Port, CancellationToken.None))
            cleanupFailure ??= new InvalidOperationException($"The isolated endpoint {Endpoint} remained responsive.");
        var sharedVersionAfter = await GetVersionAsync("http://127.0.0.1:11434", CancellationToken.None);
        attempt.AutomatedEvidence["li03.sharedRuntimeVersionAfter"] = sharedVersionAfter;
        attempt.AutomatedEvidence["li03.isolatedRuntimeStoppedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        attempt.AutomatedEvidence["li03.isolatedTeardownVerified"] =
            (cleanupFailure is null && string.Equals(sharedVersionAfter, sharedVersion, StringComparison.Ordinal))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        persist();

        if (cleanupFailure is not null)
            throw new InvalidOperationException("Isolated Ollama cleanup could not be verified.", cleanupFailure);
        if (!string.Equals(sharedVersionAfter, sharedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The shared Ollama runtime changed during the isolated evaluation.");
    }

    private static async Task<string> FindVerifiedArchiveAsync(string root)
    {
        if (!Directory.Exists(root))
            throw new InvalidOperationException("The approved Ollama archive has not been downloaded.");
        foreach (var path in Directory.EnumerateFiles(root, ArchiveName, SearchOption.AllDirectories))
        {
            var file = new FileInfo(path);
            if (file.Length != ArchiveBytes)
                continue;
            await using var stream = file.OpenRead();
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (string.Equals(digest, ArchiveSha256, StringComparison.Ordinal))
                return path;
        }

        throw new InvalidDataException("No downloaded Ollama archive matched the approved size and SHA-256.");
    }

    private static void ExtractVerifiedArchive(string archive, string destination)
    {
        using var zip = ZipFile.OpenRead(archive);
        var destinationPrefix = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The runtime archive contains a path outside its extraction root.");
        }

        zip.ExtractToDirectory(destination);
    }

    private static async Task PrepareModelStoreAsync(string destination)
    {
        var source = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ollama",
            "models");
        var sourceManifest = Path.Combine(
            source, "manifests", "registry.ollama.ai", "library", "qwen3", "1.7b");
        if (!File.Exists(sourceManifest))
            throw new InvalidOperationException("The existing pinned qwen3:1.7b manifest was not found.");

        await using (var manifestStream = File.OpenRead(sourceManifest))
        {
            var manifestDigest = Convert.ToHexString(
                await SHA256.HashDataAsync(manifestStream)).ToLowerInvariant();
            if (!string.Equals(manifestDigest, ModelDigest, StringComparison.Ordinal))
                throw new InvalidDataException("The existing qwen3:1.7b manifest digest is not the production pin.");
        }

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(sourceManifest));
        var digests = new List<string>
        {
            manifest.RootElement.GetProperty("config").GetProperty("digest").GetString()
                ?? throw new InvalidDataException("The model manifest has no config digest."),
        };
        digests.AddRange(manifest.RootElement.GetProperty("layers").EnumerateArray().Select(
            layer => layer.GetProperty("digest").GetString()
                ?? throw new InvalidDataException("A model layer has no digest.")));

        var destinationBlobs = Path.Combine(destination, "blobs");
        Directory.CreateDirectory(destinationBlobs);
        foreach (var digest in digests)
        {
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71)
                throw new InvalidDataException($"Unsupported model blob digest: {digest}");
            var fileName = digest.Replace(':', '-');
            var sourceBlob = Path.Combine(source, "blobs", fileName);
            var destinationBlob = Path.Combine(destinationBlobs, fileName);
            if (!File.Exists(sourceBlob))
                throw new InvalidDataException($"The existing model blob is missing: {fileName}");
            await using (var blobStream = File.OpenRead(sourceBlob))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(blobStream)).ToLowerInvariant();
                if (!string.Equals(actual, digest[7..], StringComparison.Ordinal))
                    throw new InvalidDataException($"The existing model blob failed digest verification: {fileName}");
            }
            if (!CreateHardLink(destinationBlob, sourceBlob, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not hard-link model blob {fileName}.");
        }

        var destinationManifest = Path.Combine(
            destination, "manifests", "registry.ollama.ai", "library", "qwen3", "1.7b");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationManifest)!);
        File.Copy(sourceManifest, destinationManifest);
    }

    private static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(2),
    })
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static async Task<string> WaitForVersionAsync(HttpClient client, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(Endpoint + "/api/version", cancellationToken);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return json.RootElement.GetProperty("version").GetString()
                    ?? throw new InvalidDataException("The isolated runtime returned no version.");
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static async Task<string> GetVersionAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var versionClient = CreateClient();
        using var response = await versionClient.GetAsync(endpoint + "/api/version", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("version").GetString()
            ?? throw new InvalidDataException($"The runtime at {endpoint} returned no version.");
    }

    private static async Task<bool> IsPortOpenAsync(int port, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task PumpAsync(StreamReader reader, string path)
    {
        await using var writer = new StreamWriter(path, append: false);
        while (await reader.ReadLineAsync() is { } line)
        {
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
