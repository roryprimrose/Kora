using System.Net;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

#pragma warning disable GHCP001 // Exercises the same pinned final-request boundary as production, without starting an SDK runtime.
public sealed class CopilotFinalRequestHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exact_final_body_headers_account_and_identity_are_reviewed_before_any_network_send()
    {
        using var network = new Network();
        using var http = new HttpClient(network);
        var requestId = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), requestId, ModelProviderSelection.CopilotCandidate);
        var profile = Profile();
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CopilotTransmission? reviewed = null;
        var handler = new CopilotFinalRequestHandler(http, provenance, profile, "sdk-session", () => true,
            (transmission, _) => { reviewed = transmission; return new(approval.Task); }, Token);
        using var request = Request();
        request.Headers.Authorization = new("Bearer", "synthetic-credential");
        request.Headers.Add("X-Request-Metadata", "review-me");
        var send = handler.SendFinalAsync(request, "sdk-session", null, request.RequestUri!.AbsoluteUri, Token);
        reviewed.Should().NotBeNull();
        reviewed!.Provenance.Should().BeSameAs(provenance);
        reviewed.Profile.Should().Be(profile);
        Encoding.UTF8.GetString(reviewed.Body.AsSpan()).Should().Be("{\"model\":\"model-1\",\"messages\":[\"original\",\"tool-reply\"]}");
        reviewed.Headers.Should().ContainKey("X-Request-Metadata").WhoseValue.Should().Be("review-me");
        reviewed.Headers.Should().NotContainKey("Authorization");
        network.Sends.Should().Be(0);
        request.Headers.Remove("X-Request-Metadata");
        request.Headers.Add("X-Request-Metadata", "unreviewed-mutation");
        using var originalContent = request.Content;
        using var replacement = new StringContent("unreviewed-content");
        request.Content = replacement;
        approval.SetResult(true);
        using var response = await send;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        network.Sends.Should().Be(1);
        network.Body.Should().Equal(reviewed.Body);
        network.Metadata.Should().Be("review-me");
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("foreign-session")]
    [InlineData("subagent")]
    [InlineData("http")]
    [InlineData("foreign-host")]
    [InlineData("query")]
    [InlineData("unknown-path")]
    [InlineData("model")]
    [InlineData("duplicate-model")]
    [InlineData("dead-authority")]
    [InlineData("host-header")]
    [InlineData("missing-credential")]
    [InlineData("malformed-json")]
    public async Task Denied_unknown_or_mismatched_final_requests_have_zero_network_effects(string failure)
    {
        using var network = new Network();
        using var http = new HttpClient(network);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi),
            ModelProviderSelection.CopilotCandidate);
        var handler = new CopilotFinalRequestHandler(http, provenance, Profile(), "sdk-session",
            () => !string.Equals(failure, "dead-authority", StringComparison.Ordinal),
            (_, _) => ValueTask.FromResult(!string.Equals(failure, "denied", StringComparison.Ordinal)), Token);
        var destination = failure switch
        {
            "http" => "http://api.githubcopilot.com/chat/completions",
            "foreign-host" => "https://attacker.example/chat/completions",
            "query" => "https://api.githubcopilot.com/chat/completions?content=secret",
            "unknown-path" => "https://api.githubcopilot.com/arbitrary",
            _ => "https://api.githubcopilot.com/chat/completions",
        };
        using var request = Request(destination, failure switch
        {
            "model" => "{\"model\":\"unselected-model\"}",
            "duplicate-model" => "{\"model\":\"unselected-model\",\"model\":\"model-1\"}",
            "malformed-json" => "{",
            _ => null,
        });
        if (string.Equals(failure, "host-header", StringComparison.Ordinal)) { request.Headers.Host = "attacker.example"; }
        if (string.Equals(failure, "missing-credential", StringComparison.Ordinal)) { request.Headers.Authorization = null; }
        await Assert.ThrowsAsync<CopilotEgressDeniedException>(() => handler.SendFinalAsync(request,
            string.Equals(failure, "foreign-session", StringComparison.Ordinal) ? "foreign" : "sdk-session",
            string.Equals(failure, "subagent", StringComparison.Ordinal) ? "parent" : null, destination, Token));
        network.Sends.Should().Be(0);
        handler.Denied.Should().BeTrue();
    }

    [Fact]
    public async Task Revocation_during_exact_review_and_cancellation_prevent_transmission()
    {
        using var network = new Network();
        using var http = new HttpClient(network);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi),
            ModelProviderSelection.CopilotCandidate);
        var live = true;
        var handler = new CopilotFinalRequestHandler(http, provenance, Profile(), "sdk-session", () => live,
            (_, _) => { live = false; return ValueTask.FromResult(true); }, Token);
        using var request = Request();
        await Assert.ThrowsAsync<CopilotEgressDeniedException>(() =>
            handler.SendFinalAsync(request, "sdk-session", null, request.RequestUri!.AbsoluteUri, Token));
        network.Sends.Should().Be(0);
    }

    [Theory]
    [InlineData(131072, true)]
    [InlineData(131073, false)]
    public async Task Final_body_budget_is_checked_at_the_exact_utf8_threshold(int bytes, bool allowed)
    {
        using var network = new Network();
        using var http = new HttpClient(network);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi),
            ModelProviderSelection.CopilotCandidate);
        var reviews = 0;
        var handler = new CopilotFinalRequestHandler(http, provenance, Profile(), "sdk-session", () => true,
            (_, _) => { reviews++; return ValueTask.FromResult(true); }, Token);
        const string prefix = "{\"model\":\"model-1\",\"padding\":\"";
        const string suffix = "\"}";
        var body = prefix + new string('x', bytes - prefix.Length - suffix.Length) + suffix;
        Encoding.UTF8.GetByteCount(body).Should().Be(bytes);
        using var request = Request(body: body);
        if (allowed)
        {
            using var response = await handler.SendFinalAsync(request, "sdk-session", null, request.RequestUri!.AbsoluteUri, Token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        else
        {
            await Assert.ThrowsAsync<CopilotEgressDeniedException>(() =>
                handler.SendFinalAsync(request, "sdk-session", null, request.RequestUri!.AbsoluteUri, Token));
            handler.Denied.Should().BeTrue();
        }
        reviews.Should().Be(allowed ? 1 : 0);
        network.Sends.Should().Be(allowed ? 1 : 0);
    }

    private static ProviderProfileTuple Profile() => new(ProviderService.Copilot, new("account"), new(1),
        new("model-1"), new("default"), new("default"));
    private static HttpRequestMessage Request(string destination = "https://api.githubcopilot.com/chat/completions", string? body = null) =>
        new(HttpMethod.Post, destination)
        {
            Headers = { Authorization = new("Bearer", "synthetic-credential") },
            Content = new StringContent(body ?? "{\"model\":\"model-1\",\"messages\":[\"original\",\"tool-reply\"]}"),
        };

    private sealed class Network : HttpMessageHandler
    {
        internal int Sends { get; private set; }
        internal byte[]? Body { get; private set; }
        internal string? Metadata { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Metadata = request.Headers.TryGetValues("X-Request-Metadata", out var values) ? values.Single() : null;
            return new(HttpStatusCode.OK) { Content = new StringContent("synthetic-http-response") };
        }
    }
}
#pragma warning restore GHCP001
