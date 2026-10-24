using System.Diagnostics;
using AwesomeAssertions;
using GitHub.Copilot;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class CopilotAccountBrokerTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public CopilotAccountBrokerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unknown_deployment_never_starts_authentication_or_infers_an_account()
    {
        var f = new Fixture { Approved = false };
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.SignInAsync("selected-user", "synthetic-token", () => true, Token));
        f.Starts.Should().Be(0);
        (await broker.ObserveAsync(Token)).Status.Should().Be(ProviderCatalogueStatus.AuthenticationRequired);
    }

    [Fact]
    public async Task Explicit_verified_token_yields_only_its_current_account_bound_models_and_revoke_removes_them()
    {
        var f = new Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync("selected-user", "synthetic-token", () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        catalogue.IsCurrent(f.Time.GetUtcNow()).Should().BeTrue();
        catalogue.Models.Should().ContainSingle().Which.Id.Value.Should().Be("model-1");
        var profile = new ProviderProfileTuple(ProviderService.Copilot, catalogue.Account!, catalogue.Revision,
            new("model-1"), new("default"), new("default"));
        var account = await broker.ResolveAsync(profile, Token);
        account.Should().NotBeNull();
        (await broker.ResolveAsync(profile, Token)).Should().BeSameAs(account);
        f.Options!.UseLoggedInUser.Should().BeFalse();
        f.Client.Stops.Should().Be(1);
        f.Client.Disposals.Should().Be(1);
        broker.Revoke();
        (await broker.ResolveAsync(profile, Token)).Should().BeNull();
        (await broker.ObserveAsync(Token)).Models.Should().BeEmpty();
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("login")]
    [InlineData("policy")]
    [InlineData("privacy")]
    [InlineData("revoked")]
    public async Task Failed_or_changed_account_evidence_is_not_a_current_catalogue(string fault)
    {
        var f = new Fixture();
        await using var broker = f.Create();
        var live = true;
        if (string.Equals(fault, "auth", StringComparison.Ordinal)) { f.Client.Auth.IsAuthenticated = false; }
        if (string.Equals(fault, "login", StringComparison.Ordinal)) { f.Client.Auth.Login = "foreign"; }
        if (string.Equals(fault, "policy", StringComparison.Ordinal)) { f.Client.Models[0].Policy!.State = "disabled"; }
        f.Client.OnModels = () =>
        {
            if (string.Equals(fault, "privacy", StringComparison.Ordinal)) { live = false; }
            if (string.Equals(fault, "revoked", StringComparison.Ordinal)) { broker.Revoke(); }
        };
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await Assert.ThrowsAnyAsync<Exception>(() => broker.SignInAsync("selected-user", "synthetic-token", () => live, Token));
        (await broker.ObserveAsync(Token)).Models.Should().BeEmpty();
        f.Client.Stops.Should().Be(1);
    }

    [Fact]
    public async Task Cancelled_authentication_stays_nonquiescent_until_stop_and_ignored_late_auth_are_joined()
    {
        var f = new Fixture();
        var pending = new TaskCompletionSource<GetAuthStatusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Authenticate = _ => pending.Task;
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var setup = broker.SignInAsync("selected-user", "synthetic-token", () => true, cancellation.Token);
        await cancellation.CancelAsync();
        await f.Client.Stopped.Task.WaitAsync(Token);
        broker.IsQuiescent.Should().BeFalse();
        setup.IsCompleted.Should().BeFalse();
        pending.SetResult(f.Client.Auth);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup);
        broker.IsQuiescent.Should().BeTrue();
        (await broker.ObserveAsync(Token)).Models.Should().BeEmpty();
    }

    private sealed class Fixture : ICopilotDeploymentApprovalSource, ISecurityAuditLog
    {
        internal bool Approved { get; set; } = true;
        internal int Starts { get; private set; }
        internal CopilotClientOptions? Options { get; private set; }
        internal FakeClient Client { get; } = new();
        internal TimeProvider Time { get; } = new FixedTime();
        private readonly CopilotDeploymentApproval deployment = new(@"Q:\owned\copilot.exe", @"Q:\owned\work",
            @"Q:\owned\state", new Dictionary<string, string>(StringComparer.Ordinal), DateTimeOffset.UnixEpoch.AddHours(1));
        internal CopilotAccountBroker Create() => new(this, Time, this, options =>
        { Starts++; Options = options; return Client; }, NullLogger<CopilotAccountBroker>.Instance);
        public ValueTask<CopilotDeploymentApproval?> ResolveAsync(CancellationToken token) => ValueTask.FromResult(Approved ? deployment : null);
        public void Write(SecurityAuditEvent auditEvent) => HostActivity.RequireCurrent();
    }
    private sealed class FakeClient : ICopilotSdkClient
    {
        internal GetAuthStatusResponse Auth { get; } = new() { IsAuthenticated = true, Login = "selected-user", Host = "github.com", AuthType = "token" };
        internal IList<ModelInfo> Models { get; } = [new() { Id = "model-1", Policy = new() { State = "enabled" },
            Capabilities = new() { Limits = new() { MaxContextWindowTokens = 8192 } } }];
        internal int Stops { get; private set; }
        internal int Disposals { get; private set; }
        internal Action? OnModels { get; set; }
        internal Func<CancellationToken, Task<GetAuthStatusResponse>>? Authenticate { get; set; }
        internal TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<GetAuthStatusResponse> AuthenticateAsync(CancellationToken token) => Authenticate?.Invoke(token) ?? Task.FromResult(Auth);
        public Task<IList<ModelInfo>> ModelsAsync(CancellationToken token) { OnModels?.Invoke(); return Task.FromResult(Models); }
        public Task<ICopilotSdkSession> CreateAsync(SessionConfig configuration, CancellationToken token) => throw new NotSupportedException();
        public Task StopAsync() { Stops++; Stopped.TrySetResult(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
