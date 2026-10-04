using System.Net;
using System.Net.Sockets;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsOllamaSetupServiceTests
{
    [Fact]
    public void Winget_arguments_pin_official_package_version_and_user_scope()
    {
        var command = OllamaProcessRunner.CreateInstallationStartInfo();

        command.FileName.Should().EndWith(
            Path.Combine("Microsoft", "WindowsApps", "winget.exe"));
        command.UseShellExecute.Should().BeFalse();
        command.ArgumentList.Should().ContainInOrder(
            "install", "--id", "Ollama.Ollama", "--exact", "--version", "0.35.1",
            "--scope", "user", "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity");
    }

    [Fact]
    public async Task InstallAsync_reuses_healthy_runtime_and_matching_model_without_processes()
    {
        var calls = new List<string>();
        var processes = new FakeProcesses();
        using var client = Client(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath switch
            {
                "/api/version" => Json("""{"version":"0.35.1"}"""),
                "/api/tags" => Json(Tags(WindowsOllamaSetupService.ModelDigest)),
                "/api/generate" => Json("""{"model":"qwen3:1.7b","done":true,"response":"OK"}"""),
                _ => throw new InvalidOperationException("Unexpected request."),
            };
        });
        var progress = new RecordingProgress();

        await new WindowsOllamaSetupService(client, processes)
            .InstallAsync(progress, TestContext.Current.CancellationToken);

        processes.Installs.Should().Be(0);
        processes.Starts.Should().Be(0);
        calls.Should().ContainInOrder("/api/version", "/api/tags", "/api/generate");
        progress.Messages.Last().Should().Contain("verified successfully");
    }

    [Fact]
    public async Task InstallAsync_installs_pinned_user_package_pulls_and_verifies_inference()
    {
        var processes = new FakeProcesses();
        var tags = 0;
        var calls = new List<string>();
        using var client = Client(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath switch
            {
                "/api/version" when processes.Installs == 0 => throw new HttpRequestException(
                    "Connection refused.", new SocketException((int)SocketError.ConnectionRefused)),
                "/api/version" => Json("""{"version":"0.35.1"}"""),
                "/api/tags" => Json(++tags == 1 ? """{"models":[]}""" : Tags(WindowsOllamaSetupService.ModelDigest)),
                "/api/pull" => Json("""{"status":"downloading","total":4,"completed":3}""" + "\n" + """{"status":"success"}""" + "\n"),
                "/api/generate" => Json("""{"model":"qwen3:1.7b","done":true,"response":"OK"}"""),
                _ => throw new InvalidOperationException("Unexpected request."),
            };
        });
        var progress = new RecordingProgress();

        await new WindowsOllamaSetupService(client, processes)
            .InstallAsync(progress, TestContext.Current.CancellationToken);

        processes.Installs.Should().Be(1);
        processes.Starts.Should().Be(0);
        calls.Should().ContainInOrder("/api/pull", "/api/tags", "/api/generate");
        progress.Messages.Should().Contain(message => message.Contains("1.36 GB", StringComparison.Ordinal));
        progress.Updates.Should().Contain(update => update.Percentage == 75);
    }

    [Fact]
    public async Task InstallAsync_rejects_existing_wrong_digest_without_replacing_it()
    {
        var processes = new FakeProcesses();
        using var client = Client(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => Json("""{"version":"0.35.1"}"""),
            "/api/tags" => Json(Tags("sha256:wrong")),
            _ => throw new InvalidOperationException("Must not pull or generate."),
        });
        var progress = new RecordingProgress();

        var action = () => new WindowsOllamaSetupService(client, processes)
            .InstallAsync(progress, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*different or missing digest*");
        processes.Installs.Should().Be(0);
        progress.Messages.Should().NotContain(message => message.Contains("verified successfully", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallAsync_does_not_install_over_an_incompatible_local_endpoint()
    {
        var processes = new FakeProcesses();
        using var client = Client(_ => Json("""{"unexpected":true}"""));
        var progress = new RecordingProgress();
        var action = () => new WindowsOllamaSetupService(client, processes)
            .InstallAsync(progress, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not a compatible Ollama runtime*");
        processes.Installs.Should().Be(0);
        processes.Starts.Should().Be(0);
    }

    [Fact]
    public async Task InstallAsync_rejects_failed_download_even_with_http_success()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => Json("""{"version":"0.35.1"}"""),
            "/api/tags" => Json("""{"models":[]}"""),
            "/api/pull" => Json("""{"error":"manifest unavailable"}""" + "\n"),
            _ => throw new InvalidOperationException("Must not generate."),
        });
        var progress = new RecordingProgress();
        var action = () => new WindowsOllamaSetupService(client, new FakeProcesses())
            .InstallAsync(progress, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*manifest unavailable*");
        progress.Messages.Should().NotContain(message => message.Contains("verified successfully", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallAsync_propagates_cancellation_without_starting_any_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var processes = new FakeProcesses();
        using var client = Client(_ => throw new InvalidOperationException("No HTTP request expected."));

        var action = () => new WindowsOllamaSetupService(client, processes)
            .InstallAsync(new RecordingProgress(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        processes.Installs.Should().Be(0);
        processes.Starts.Should().Be(0);
    }

    [Fact]
    public async Task InstallAsync_rejects_empty_generation_response()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => Json("""{"version":"0.35.1"}"""),
            "/api/tags" => Json(Tags(WindowsOllamaSetupService.ModelDigest)),
            "/api/generate" => Json("""{"model":"qwen3:1.7b","done":true,"response":""}"""),
            _ => throw new InvalidOperationException("Unexpected request."),
        });
        var progress = new RecordingProgress();
        var action = () => new WindowsOllamaSetupService(client, new FakeProcesses())
            .InstallAsync(progress, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*nonempty response*");
        progress.Messages.Should().NotContain(message => message.Contains("verified successfully", StringComparison.Ordinal));
    }

    private static string Tags(string digest) =>
        $$"""{"models":[{"name":"qwen3:1.7b","digest":"{{digest}}"}]}""";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new StubHandler(send));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class FakeProcesses : IOllamaProcessRunner
    {
        public int Installs { get; private set; }

        public int Starts { get; private set; }

        public bool HasInstalledRuntime(string executable) => false;

        public Task<int> InstallWingetAsync(CancellationToken cancellationToken)
        {
            Installs++;
            return Task.FromResult(0);
        }

        public void StartServer(string executable) => Starts++;

        public void StopOwnedServer() { }
    }

    private sealed class RecordingProgress : IProgress<LocalModelSetupProgress>
    {
        public List<string> Messages { get; } = [];

        public List<LocalModelSetupProgress> Updates { get; } = [];

        public void Report(LocalModelSetupProgress value)
        {
            Updates.Add(value);
            Messages.Add(value.Detail);
        }
    }
}
