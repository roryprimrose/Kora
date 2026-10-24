using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using GitHub.Copilot;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class CopilotSdkTurnRuntimeTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public CopilotSdkTurnRuntimeTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_account_never_constructs_an_sdk_client_or_uses_host_sign_in()
    {
        var fixture = new Fixture { AccountAvailable = false };
        using var root = fixture.Root();
        var reply = await fixture.Run(Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.AuthenticationRequired);
        fixture.Options.Should().BeNull();
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("foreign-login")]
    [InlineData("gh-cli")]
    [InlineData("foreign-host")]
    public async Task Unknown_or_foreign_authentication_fails_closed_before_catalogue_or_session(string failure)
    {
        var fixture = new Fixture();
        fixture.Client.Auth = new() { IsAuthenticated = !string.Equals(failure, "denied", StringComparison.Ordinal),
            Login = string.Equals(failure, "foreign-login", StringComparison.Ordinal) ? "foreign" : "selected-user",
            AuthType = string.Equals(failure, "gh-cli", StringComparison.Ordinal) ? "gh-cli" : "token",
            Host = string.Equals(failure, "foreign-host", StringComparison.Ordinal) ? "elsewhere.example" : "github.com" };
        using var root = fixture.Root();
        (await fixture.Run(Token)).Outcome.Should().Be(ModelTurnOutcome.AuthenticationRequired);
        fixture.Client.ModelReads.Should().Be(0);
        fixture.Client.Configuration.Should().BeNull();
        fixture.Client.Stops.Should().Be(1);
        fixture.Client.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task A_stale_catalogue_never_sends_or_falls_back()
    {
        var fixture = new Fixture();
        fixture.Client.Models = [];
        using var root = fixture.Root();
        (await fixture.Run(Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        fixture.Client.Configuration.Should().BeNull();
        fixture.Client.Prompt.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("disabled")]
    [InlineData("unknown")]
    public async Task Missing_or_denied_model_policy_does_not_create_a_session(string? state)
    {
        var fixture = new Fixture();
        fixture.Client.Models[0].Policy = state is null ? null : new() { State = state };
        using var root = fixture.Root();
        (await fixture.Run(Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        fixture.Client.Configuration.Should().BeNull();
    }

#pragma warning disable GHCP001 // Supplies actual SDK callback context to the deterministic client boundary.
    [Theory]
    [InlineData("valid")]
    [InlineData("foreign-session")]
    [InlineData("subagent")]
    [InlineData("wrong-name")]
    [InlineData("oversize")]
    [InlineData("revoked-account")]
    public async Task Sdk_callbacks_reenter_exact_host_context_and_reject_foreign_or_malicious_requests(string scenario)
    {
        var fixture = new Fixture();
        var calls = 0;
        fixture.Tool = (id, input, _) =>
        {
            calls++;
            id.Should().Be(ReadOnlyCapabilityCatalog.Version);
            input.Should().Be("{}");
            HostActivity.RequireCurrent().Request.Should().BeSameAs(fixture.Request);
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "synthetic-version"));
        };
        fixture.Client.Send = async token =>
        {
            var function = fixture.Client.Configuration!.Tools!.Single(item =>
                string.Equals(item.Name, "kora_application_get_version", StringComparison.Ordinal)).Should().BeAssignableTo<AIFunction>().Which;
            var arguments = new AIFunctionArguments(StringComparer.Ordinal)
            {
                ["input"] = string.Equals(scenario, "oversize", StringComparison.Ordinal) ? new string('x', 1025) : "{}",
                Context = new Dictionary<object, object?>
                {
                    [typeof(ToolInvocation)] = new ToolInvocation
                    {
                        SessionId = string.Equals(scenario, "foreign-session", StringComparison.Ordinal) ? "foreign" : fixture.Client.SessionId,
                        ToolName = string.Equals(scenario, "wrong-name", StringComparison.Ordinal) ? "computer.lock" : function.Name,
                        AgentId = string.Equals(scenario, "subagent", StringComparison.Ordinal) ? "subagent" : null,
                    },
                },
            };
            if (string.Equals(scenario, "revoked-account", StringComparison.Ordinal)) { fixture.AccountAvailable = false; }
            using var untrusted = new Activity("untrusted-sdk-context").SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start();
            var value = await function.InvokeAsync(arguments, token);
            var serialized = value.Should().BeOfType<JsonElement>().Which;
            serialized.ValueKind.Should().Be(JsonValueKind.String);
            using var document = JsonDocument.Parse(serialized.GetString() ?? throw new InvalidDataException("The SDK callback result is missing."));
            document.RootElement.GetProperty("Outcome").GetInt32().Should().Be(
                string.Equals(scenario, "valid", StringComparison.Ordinal) ? (int)CapabilityOutcome.Succeeded : (int)CapabilityOutcome.Denied);
            return "bounded-answer";
        };
        using var root = fixture.Root();
        await fixture.Run(Token);
        calls.Should().Be(string.Equals(scenario, "valid", StringComparison.Ordinal) ? 1 : 0);
    }
#pragma warning restore GHCP001

    [Fact]
    public async Task Original_request_and_host_turn_identity_are_preserved_and_native_facilities_are_disabled()
    {
        var fixture = new Fixture();
        using var root = fixture.Root();
        var reply = await fixture.Run(Token);
        reply.Should().Be(new CopilotTurnReply(ModelTurnOutcome.Succeeded, "genuine-sdk-boundary-answer"));
        fixture.Client.Prompt.Should().Be("{\"UserRequest\":\"original-session-request\"}");
        fixture.Options!.Mode.Should().Be(CopilotClientMode.Empty);
        fixture.Options.UseLoggedInUser.Should().BeFalse();
        fixture.Options.Environment.Should().BeEmpty();
        fixture.Options.BuiltinPluginDirectories.Should().BeEmpty();
        var configuration = fixture.Client.Configuration!;
        configuration.SessionId.Should().Be(fixture.Provenance.Turn.Value.ToString("D"));
        configuration.AvailableTools.Should().HaveCount(ReadOnlyCapabilityCatalog.Descriptors.Count);
        configuration.ExcludedTools.Should().Equal("builtin:*", "mcp:*");
        configuration.EnableConfigDiscovery.Should().BeFalse();
        configuration.SkipCustomInstructions.Should().BeTrue();
        configuration.EnableSessionStore.Should().BeFalse();
        configuration.EnableSkills.Should().BeFalse();
        configuration.EnableSessionTelemetry.Should().BeFalse();
        configuration.EnableHostGitOperations.Should().BeFalse();
        configuration.EnableFileHooks.Should().BeFalse();
        configuration.SkipEmbeddingRetrieval.Should().BeTrue();
        configuration.InfiniteSessions!.Enabled.Should().BeFalse();
        configuration.McpServers.Should().BeEmpty();
        configuration.CustomAgents.Should().BeEmpty();
        configuration.PluginDirectories.Should().BeEmpty();
        configuration.CreateSessionFsProvider.Should().NotBeNull();
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Empty_or_missing_answers_are_not_success_shaped(string? answer)
    {
        var fixture = new Fixture();
        fixture.Client.Send = _ => Task.FromResult(answer);
        using var root = fixture.Root();
        var reply = await fixture.Run(Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        reply.Answer.Should().BeNull();
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(131072)]
    public async Task Oversize_answers_are_never_returned_or_retained(int length)
    {
        var fixture = new Fixture();
        fixture.Client.Send = _ => Task.FromResult<string?>(new string('x', length));
        using var root = fixture.Root();
        (await fixture.Run(Token)).Should().Be(new CopilotTurnReply(ModelTurnOutcome.Unknown));
    }

    [Fact]
    public async Task Foreign_sdk_session_cannot_send_under_the_original_host_identity()
    {
        var fixture = new Fixture();
        fixture.Client.ForeignSession = true;
        using var root = fixture.Root();
        (await fixture.Run(Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Client.Prompt.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_keeps_slot_until_real_stop_and_late_send_join_then_discards_answer()
    {
        var fixture = new Fixture();
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Send = _ => answer.Task;
        fixture.Client.Stop = () => stop.Task;
        using var root = fixture.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = fixture.Run(cancellation.Token);
        fixture.Client.Sent.Should().BeTrue();
        await cancellation.CancelAsync();
        await fixture.Client.Stopping.Task.WaitAsync(Token);
        fixture.Runtime.IsQuiescent.Should().BeFalse();
        run.IsCompleted.Should().BeFalse();
        (await fixture.Run(Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        stop.SetResult();
        answer.SetResult("late-output-must-not-be-retained");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_stop_is_not_quiescence_and_blocks_subsequent_turns()
    {
        var fixture = new Fixture();
        fixture.Client.Stop = () => Task.FromException(new IOException("synthetic-stop-failure"));
        using var root = fixture.Root();
        await Assert.ThrowsAsync<IOException>(() => fixture.Run(Token));
        fixture.Runtime.IsQuiescent.Should().BeFalse();
        (await fixture.Run(Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        fixture.Client.Stop = () => Task.CompletedTask;
        await fixture.Runtime.DisposeAsync();
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task A_throwing_sdk_cancellation_callback_cannot_skip_owned_stop_or_disposal()
    {
        var fixture = new Fixture();
        CancellationTokenRegistration registration = default;
        fixture.Client.Send = token =>
        {
            registration = token.Register(() => throw new InvalidOperationException("synthetic-cancellation-callback"));
            return Task.FromResult<string?>("answer-must-not-be-retained");
        };
        using var root = fixture.Root();
        try
        {
            await Assert.ThrowsAsync<AggregateException>(() => fixture.Run(Token));
            fixture.Client.Stops.Should().Be(1);
            fixture.Client.Disposals.Should().Be(1);
            fixture.Runtime.IsQuiescent.Should().BeTrue();
        }
        finally { await registration.DisposeAsync(); }
    }

    [Fact]
    public async Task Retried_failed_stop_cannot_release_ownership_before_its_late_send_has_ended()
    {
        var fixture = new Fixture();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Send = _ => pending.Task;
        fixture.Client.Stop = () => Task.FromException(new IOException("synthetic-stop-failure"));
        using var root = fixture.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var running = fixture.Run(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<IOException>(() => running);
        fixture.Client.Stop = () => Task.CompletedTask;
        var disposal = fixture.Runtime.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        fixture.Runtime.IsQuiescent.Should().BeFalse();
        pending.SetResult("late-answer");
        await disposal;
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unreviewed_or_denied_sdk_answers_cannot_be_reported_as_hosted_success(bool bypass)
    {
        var fixture = new Fixture { BypassTransmission = bypass, ApproveTransmission = false };
        using var root = fixture.Root();
        var result = await fixture.Run(Token);
        result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Answer.Should().BeNull();
        fixture.Network.Sends.Should().Be(0);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    private sealed class Fixture : ICopilotExecutionAccountSource
    {
        internal HostRequest Request { get; } = HostRequest.Create(RequestOrigin.LocalUi);
        internal ModelTurnProvenance Provenance { get; }
        internal ProviderProfileTuple Profile { get; } = new(ProviderService.Copilot, new("selected-account"), new(7),
            new("model-1"), new("default"), new("default"));
        internal CopilotSdkTurnRuntime Runtime { get; }
        internal FakeClient Client { get; } = new();
        internal CopilotClientOptions? Options { get; private set; }
        internal bool AccountAvailable { get; set; } = true;
        internal bool ApproveTransmission { get; set; } = true;
        internal bool BypassTransmission { get; set; }
        internal FakeNetwork Network { get; } = new();
        internal Func<string, string, CancellationToken, ValueTask<CapabilityReply>> Tool { get; set; } =
            (_, _, _) => ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Denied, "test-denied"));
        private readonly CopilotExecutionAccount account;

        internal Fixture()
        {
            Provenance = new(new(Guid.NewGuid()), Request, ModelProviderSelection.CopilotCandidate);
            account = new(Profile, "selected-user", "synthetic-not-a-secret", @"Q:\owned\copilot-runtime.exe",
                @"Q:\owned\working", @"Q:\owned\state", new Dictionary<string, string>(StringComparer.Ordinal), DateTimeOffset.UnixEpoch.AddMinutes(10));
            Runtime = new(this, new FixedTime(), NullLogger<CopilotSdkTurnRuntime>.Instance, options =>
            { Options = options; return Client; }, () => Network);
            Client.BeforeSend = async token =>
            {
                if (BypassTransmission) { return; }
#pragma warning disable GHCP001 // Calls the same pinned final-request gate through the deterministic SDK boundary.
                var gate = Options!.RequestHandler.Should().BeOfType<CopilotFinalRequestHandler>().Which;
#pragma warning restore GHCP001
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions")
                {
                    Headers = { Authorization = new("Bearer", "synthetic-credential") },
                    Content = new StringContent("{\"model\":\"model-1\",\"messages\":[\"original-session-request\"]}"),
                };
                using var response = await gate.SendFinalAsync(request, Client.SessionId, null, request.RequestUri!.AbsoluteUri, token);
            };
        }

        public ValueTask<CopilotExecutionAccount?> ResolveAsync(ProviderProfileTuple profile, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AccountAvailable ? account : null);
        internal HostActivity Root() => HostActivity.BeginRoot(Request, HostActivityLayer.Windows, HostOperation.Request);
        internal Task<CopilotTurnReply> Run(CancellationToken token) => Runtime.RunAsync(Provenance, Profile,
            Encoding.UTF8.GetBytes("{\"UserRequest\":\"original-session-request\"}"),
            Tool,
            (_, _) => ValueTask.FromResult(ApproveTransmission), token);
    }

    private sealed class FakeClient : ICopilotSdkClient, ICopilotSdkSession
    {
        internal GetAuthStatusResponse Auth { get; set; } = new()
        { IsAuthenticated = true, Login = "selected-user", AuthType = "token", Host = "github.com" };
        internal IList<ModelInfo> Models { get; set; } = [new() { Id = "model-1", Policy = new() { State = "enabled" },
            Capabilities = new() { Limits = new() { MaxContextWindowTokens = 8192 } } }];
        internal SessionConfig? Configuration { get; private set; }
        internal string? Prompt { get; private set; }
        internal bool ForeignSession { get; set; }
        internal bool Sent { get; private set; }
        internal int ModelReads { get; private set; }
        internal int Stops { get; private set; }
        internal int Disposals { get; private set; }
        internal TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<CancellationToken, Task<string?>> Send { get; set; } =
            _ => Task.FromResult<string?>("genuine-sdk-boundary-answer");
        internal Func<Task> Stop { get; set; } = () => Task.CompletedTask;
        internal Func<CancellationToken, Task> BeforeSend { get; set; } = _ => Task.CompletedTask;
        public string SessionId => ForeignSession ? "foreign-session" : Configuration!.SessionId!;
        public Task<GetAuthStatusResponse> AuthenticateAsync(CancellationToken token) => Task.FromResult(Auth);
        public Task<IList<ModelInfo>> ModelsAsync(CancellationToken token)
        { ModelReads++; return Task.FromResult(Models); }
        public Task<ICopilotSdkSession> CreateAsync(SessionConfig configuration, CancellationToken token)
        { Configuration = configuration; return Task.FromResult<ICopilotSdkSession>(this); }
        public async Task<string?> SendAsync(string prompt, CancellationToken token)
        { Prompt = prompt; Sent = true; await BeforeSend(token); return await Send(token); }
        public Task StopAsync()
        { Stops++; Stopping.TrySetResult(); return Stop(); }
        public ValueTask DisposeAsync()
        { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class FakeNetwork : HttpMessageHandler
    {
        internal int Sends { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("synthetic-http-answer") });
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
