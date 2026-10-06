using System.Net;
using System.Net.Sockets;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class LocalInferenceDependencyProbeTests
{
    [Fact]
    public async Task ProbeAsync_does_not_claim_inference_ready_merely_because_models_exist()
    {
        using var client = new HttpClient(new StubHandler(request =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => Json("""{"version":"0.35.1"}"""),
                "/api/tags" => Json("""{"models":[{"name":"example:latest"}]}"""),
                _ => throw new InvalidOperationException("Unexpected endpoint"),
            })));
        var probe = new LocalInferenceDependencyProbe(client);

        var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.NeedsConfiguration);
        status.Detail.Should().Contain("qwen3:1.7b");
    }

    [Fact]
    public async Task ProbeAsync_marks_pinned_model_ready_only_after_real_inference_check()
    {
        using var client = new HttpClient(new StubHandler(request =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => Json("""{"version":"0.35.1"}"""),
                "/api/tags" => Json("""{"models":[{"name":"qwen3:1.7b","digest":"8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7"}]}"""),
                "/api/generate" => Json("""{"done":true,"response":"OK"}"""),
                _ => throw new InvalidOperationException("Unexpected endpoint"),
            })));
        var probe = new LocalInferenceDependencyProbe(client);

        var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Ready);
        status.Detail.Should().Contain("passed");
    }

    [Fact]
    public async Task ProbeAsync_detects_a_changed_model_digest_on_a_later_run()
    {
        using var client = new HttpClient(new StubHandler(request =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => Json("""{"version":"0.35.1"}"""),
                "/api/tags" => Json("""{"models":[{"name":"qwen3:1.7b","digest":"sha256:changed"}]}"""),
                _ => throw new InvalidOperationException("Inference must not be attempted"),
            })));

        var status = await new LocalInferenceDependencyProbe(client)
            .ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Incompatible);
        status.Detail.Should().Contain("unexpected digest");
    }

    [Fact]
    public async Task ProbeAsync_reports_missing_runtime_without_installing_anything()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("refused",
                    new SocketException((int)SocketError.ConnectionRefused)))));
        var probe = new LocalInferenceDependencyProbe(client);

        var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Missing);
    }

    [Fact]
    public async Task ProbeAsync_rejects_a_non_ollama_endpoint()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Task.FromResult(Json("""{"unexpected":true}"""))));
        var probe = new LocalInferenceDependencyProbe(client);

        var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Incompatible);
    }

    [Theory]
    [InlineData("[]", """{"models":[]}""")]
    [InlineData("""{"version":"0.35.1"}""", "[]")]
    [InlineData("""{"version":"0.35.1"}""", """{"models":[null,12]}""")]
    [InlineData("""{"version":"0.35.1"}""", """{"models":[{"name":"qwen3:1.7b","digest":12}]}""")]
    public async Task ProbeAsync_reports_invalid_endpoint_metadata_as_incompatible(
        string version, string tags)
    {
        using var client = new HttpClient(new StubHandler(request =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => Json(version),
                "/api/tags" => Json(tags),
                _ => throw new InvalidOperationException("Inference must not be attempted"),
            })));

        var status = await new LocalInferenceDependencyProbe(client)
            .ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Incompatible);
    }

    [Fact]
    public async Task ProbeAsync_reports_invalid_generation_response_as_failed()
    {
        using var client = new HttpClient(new StubHandler(request =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => Json("""{"version":"0.35.1"}"""),
                "/api/tags" => Json(Tags(WindowsOllamaSetupService.ModelDigest)),
                "/api/generate" => Json("[]"),
                _ => throw new InvalidOperationException("Unexpected endpoint"),
            })));

        var status = await new LocalInferenceDependencyProbe(client)
            .ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Failed);
    }

    private static string Tags(string digest) =>
        $$"""{"models":[{"name":"qwen3:1.7b","digest":"{{digest}}"}]}""";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
