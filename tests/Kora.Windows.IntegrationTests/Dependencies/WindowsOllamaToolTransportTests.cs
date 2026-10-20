using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed partial class WindowsOllamaToolTransportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Model = "deterministic-fake:one";
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Final = """{"model":"deterministic-fake:one","message":{"role":"assistant","content":"Genuine answer"},"done":true,"done_reason":"stop"}""";
    private const string Call = """{"model":"deterministic-fake:one","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"application.get_version","arguments":{}}}]},"done":true}""";

    [Fact]
    public async Task Native_tool_call_and_inert_result_continue_once_to_a_genuine_final_answer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, fixture.Bodies.Count == 1 ? Call : Final));
        var first = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        var calls = first.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        calls.Calls.Should().ContainSingle().Which.CallId.Should().Be("ollama_r1_c0");
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0],
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.2.3", "not-observed")));
        var second = await fixture.Transport.CompleteAsync(fixture.Request([calls, result], 2), Token);
        second.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        second.Provenance.Should().BeSameAs(fixture.Provenance);
        second.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Genuine answer");
        fixture.Paths.Should().Equal("/api/version", "/api/tags", "/api/chat", "/api/version", "/api/tags", "/api/chat");
        using var sent = JsonDocument.Parse(fixture.Bodies[1]);
        var messages = sent.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        messages.Select(message => message.GetProperty("role").GetString()).Should().Equal("system", "user", "assistant", "tool");
        messages[3].GetProperty("tool_name").GetString().Should().Be(ReadOnlyCapabilityCatalog.Version);
        messages[3].GetProperty("content").GetString().Should().Be(Encoding.UTF8.GetString(result.Result.AsSpan()));
        var wire = Encoding.UTF8.GetString(fixture.Bodies[1]);
        wire.Should().NotContain(fixture.Provenance.Request.SessionId.Value.ToString("D"));
        wire.Should().NotContain(fixture.Profile.Account.Value);
        sent.RootElement.GetProperty("tools").GetArrayLength().Should().Be(ReadOnlyCapabilityCatalog.Descriptors.Count);
        sent.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("think").GetBoolean().Should().BeFalse();
        (await fixture.Transport.CompleteAsync(fixture.Request([calls, result], 2), Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Bodies.Should().HaveCount(2);
    }

    [Fact]
    public async Task Provider_call_ID_is_correlation_only_and_returned_without_changing_the_tool_name()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, fixture.Bodies.Count == 1
            ? Call.Replace("""{"function":""", """{"id":"provider_1","type":"function","function":""", StringComparison.Ordinal)
            : Final));
        var first = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        var calls = first.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0], VersionReply());
        (await fixture.Transport.CompleteAsync(fixture.Request([calls, result], 2), Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        using var sent = JsonDocument.Parse(fixture.Bodies[1]);
        sent.RootElement.GetProperty("messages")[3].GetProperty("tool_call_id").GetString().Should().Be("provider_1");
    }

    [Fact]
    public async Task Missing_host_binding_is_unavailable_without_any_observation_or_HTTP()
    {
        await using var fixture = new Fixture { MissingBinding = true };
        using var root = fixture.Root();
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        reply.Message.Should().BeNull();
        fixture.Observations.Should().Be(0);
        fixture.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_host_activity_never_sends()
    {
        await using var fixture = new Fixture();
        (await fixture.Transport.CompleteAsync(fixture.Request(), Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_profile_has_no_implicit_model_or_account_choice()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var context = fixture.Request().Context;
        var request = ModelToolProtocol.CreateRequest(fixture.Provenance, null, context, [], 1);
        var reply = await fixture.Transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        reply.Message.Should().BeNull();
        fixture.Observations.Should().Be(0);
        fixture.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Foreign_session_provenance_cannot_borrow_the_current_host_activity()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var foreign = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi),
            ModelProviderSelection.OllamaCandidate);
        var context = new ModelContextEnvelope(foreign.Request, "host policy", "other request", [],
            fixture.Time.GetUtcNow(), fixture.Time.GetUtcNow().AddMinutes(1));
        var request = ModelToolProtocol.CreateRequest(foreign, fixture.Profile,
            ModelToolProtocol.ReadContext(context.Serialize(), foreign), [], 1);
        (await fixture.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Observations.Should().Be(0);
        fixture.Paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admission")]
    [InlineData("profile")]
    [InlineData("expiry")]
    [InlineData("revoked")]
    public async Task Unknown_or_changed_source_identity_is_rejected_before_HTTP(string failure)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        if (string.Equals(failure, "owner", StringComparison.Ordinal)) { fixture.ProcessCurrent = false; }
        if (string.Equals(failure, "admission", StringComparison.Ordinal)) { fixture.Admitted = false; }
        if (string.Equals(failure, "expiry", StringComparison.Ordinal)) { fixture.Expired = true; }
        if (string.Equals(failure, "revoked", StringComparison.Ordinal)) { await fixture.Revoked.CancelAsync(); }
        var request = fixture.Request(profile: string.Equals(failure, "profile", StringComparison.Ordinal)
            ? fixture.Profile with { Model = new("different") } : fixture.Profile);
        (await fixture.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Production_HTTP_policy_has_no_proxy_redirect_cookie_credentials_or_decompression()
    {
        using var handler = WindowsOllamaToolTransport.CreateHandler();
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
        handler.UseCookies.Should().BeFalse();
        handler.UseDefaultCredentials.Should().BeFalse();
        handler.Credentials.Should().BeNull();
        handler.AutomaticDecompression.Should().Be(DecompressionMethods.None);
    }

    private static HttpResponseMessage Json(HttpRequestMessage request, string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { RequestMessage = request, Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private static CapabilityReply VersionReply() =>
        new(CapabilityOutcome.Succeeded, "observed", Version: new("1.2.3", "not-observed"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal readonly HostRequest RequestIdentity = HostRequest.Create(RequestOrigin.LocalUi);
        internal readonly ProviderProfileTuple Profile = new(ProviderService.Local, new("fake-account-binding"),
            new(1), new("fake-model-choice"), new("off"), new("bounded"));
        internal readonly Clock Time = new();
        internal readonly CancellationTokenSource Revoked = new();
        internal readonly List<string> Paths = [];
        internal readonly List<byte[]> Bodies = [];
        internal bool MissingBinding;
        internal bool ProcessCurrent = true;
        internal bool Admitted = true;
        internal bool Expired;
        internal int Observations;
        internal string BoundModel = Model;
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Chat =
            (message, _) => Task.FromResult(Json(message, Final));
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Metadata;
        internal readonly ModelTurnProvenance Provenance;
        internal readonly WindowsOllamaToolTransport Transport;
        internal readonly Handler HttpHandler;
        internal Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            Provenance = new(new(Guid.NewGuid()), RequestIdentity, ModelProviderSelection.OllamaCandidate);
            HttpHandler = new(async (message, token) =>
            {
                message.RequestUri!.Scheme.Should().Be("http");
                message.RequestUri.Host.Should().Be("127.0.0.1");
                message.RequestUri.Port.Should().Be(11434);
                message.Headers.Should().BeEmpty();
                Paths.Add(message.RequestUri.AbsolutePath);
                if (string.Equals(message.RequestUri.AbsolutePath, "/api/chat", StringComparison.Ordinal))
                {
                    Bodies.Add(await message.Content!.ReadAsByteArrayAsync(token));
                    return await Chat(message, token);
                }
                if (Metadata is not null) { return await Metadata(message, token); }
                return Json(message, string.Equals(message.RequestUri.AbsolutePath, "/api/version", StringComparison.Ordinal)
                    ? """{"version":"fake-runtime"}"""
                    : JsonSerializer.Serialize(new { models = new[] { new { name = BoundModel, digest = Digest } } }));
            });
            Transport = new(new HttpClient(HttpHandler), request => MissingBinding ? null : Binding(),
                (_, token) => { token.ThrowIfCancellationRequested(); Observations++; return Task.FromResult(ProcessCurrent); },
                Time, NullLogger<WindowsOllamaToolTransport>.Instance);
        }
        internal WindowsOllamaToolEndpointBinding Binding() => new(Provenance, Profile, BoundModel, Digest, "fake-runtime",
            4242, Time.GetUtcNow(), @"C:\fake\ollama.exe", Digest,
            Expired ? Time.GetUtcNow() : Time.GetUtcNow().AddMinutes(1), () => Admitted, Revoked.Token);
        internal HostActivity Root() => HostActivity.BeginRoot(RequestIdentity, HostActivityLayer.Application, HostOperation.Runtime);
        internal ModelToolRequest Request(IEnumerable<ModelToolMessage>? messages = null, int round = 1,
            ProviderProfileTuple? profile = null, string user = "Return the actual application version.",
            ModelToolBudget? budget = null, IEnumerable<ModelContextEvidence>? evidence = null,
            ModelToolMode toolMode = ModelToolMode.ReadOnly)
        {
            var envelope = new ModelContextEnvelope(RequestIdentity, "Tools are host-mediated observations.", user,
                evidence ?? [], Time.GetUtcNow(), Time.GetUtcNow().AddMinutes(1), toolMode);
            return ModelToolProtocol.CreateRequest(Provenance, profile ?? Profile,
                ModelToolProtocol.ReadContext(envelope.Serialize(), Provenance), messages ?? [], round, budget);
        }
        public async ValueTask DisposeAsync()
        {
            await Transport.DisposeAsync();
            Revoked.Dispose();
            listener.Dispose();
        }
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 10, 19, 20, 30, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
