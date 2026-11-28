using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class HostedLeaseRenewalTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public HostedLeaseRenewalTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Copilot_renewal_extends_the_same_account_in_place_with_audit_and_no_reauthentication()
    {
        var f = new CopilotAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync("selected-user", "synthetic-token", () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = new ProviderProfileTuple(ProviderService.Copilot, catalogue.Account!, catalogue.Revision,
            new("model-1"), new("default"), new("default"));
        var account = (await broker.ResolveAsync(profile, Token))!;
        account.ExpiresAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(5));
        var starts = f.Starts;
        f.Audits.Clear();
        f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(4).AddSeconds(30);
        (await broker.RenewAsync(account, Token)).Should().BeTrue();
        account.ExpiresAt.Should().Be(f.Time.Now.AddMinutes(5));
        var renewed = await broker.ObserveAsync(Token);
        renewed.Revision.Should().Be(catalogue.Revision, "renewal never changes the catalogue revision or profile tuple");
        renewed.ValidUntil.Should().Be(account.ExpiresAt);
        (await broker.ResolveAsync(profile, Token)).Should().BeSameAs(account);
        f.Starts.Should().Be(starts, "renewal neither starts the SDK nor re-authenticates");
        f.Audits.Select(item => (item.ActionId, item.Outcome, item.Initiator)).Should().Equal(
            ("model.copilot.lease-renew", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System),
            ("model.copilot.lease-renew", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.System));
        f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(9);
        (await broker.ResolveAsync(profile, Token)).Should().BeSameAs(account, "the renewed lease outlives the original five minutes");
    }

    [Theory]
    [InlineData("approval-cap")]
    [InlineData("unapproved")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("foreign")]
    [InlineData("no-host")]
    public async Task Copilot_renewal_refuses_without_the_same_current_approval_lease_and_host_context(string fault)
    {
        var f = new CopilotAccountBrokerTests.Fixture
        {
            ApprovalExpiresAt = fault is "approval-cap" ? DateTimeOffset.UnixEpoch.AddMinutes(5) : DateTimeOffset.UnixEpoch.AddHours(1),
        };
        await using var broker = f.Create();
        CopilotExecutionAccount account;
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request))
        {
            await broker.SignInAsync("selected-user", "synthetic-token", () => true, Token);
            var catalogue = await broker.ObserveAsync(Token);
            account = (await broker.ResolveAsync(new(ProviderService.Copilot, catalogue.Account!, catalogue.Revision,
                new("model-1"), new("default"), new("default")), Token))!;
        }
        var expiresAt = account.ExpiresAt;
        f.Audits.Clear();
        f.Time.Now = fault is "expired" ? DateTimeOffset.UnixEpoch.AddMinutes(5) : DateTimeOffset.UnixEpoch.AddMinutes(4);
        if (fault is "unapproved") { f.Approved = false; }
        if (fault is "revoked") { broker.Revoke(); }
        var target = fault is "foreign" ? new CopilotExecutionAccount(account.Profile, account.Login, "other-token",
            @"Q:\owned\copilot.exe", @"Q:\owned\work", @"Q:\owned\state", new Dictionary<string, string>(StringComparer.Ordinal),
            account.ExpiresAt) : account;
        using var root = fault is "no-host" ? null
            : HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        (await broker.RenewAsync(target, Token)).Should().BeFalse();
        account.ExpiresAt.Should().Be(expiresAt);
        f.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Copilot_unconfirmed_renewal_audit_restores_the_previous_lease()
    {
        var f = new CopilotAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync("selected-user", "synthetic-token", () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var account = (await broker.ResolveAsync(new(ProviderService.Copilot, catalogue.Account!, catalogue.Revision,
            new("model-1"), new("default"), new("default")), Token))!;
        f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(4);
        f.FailWrite = item => item.Outcome == SecurityAuditOutcome.Succeeded;
        await broker.Invoking(value => value.RenewAsync(account, Token).AsTask()).Should().ThrowAsync<IOException>();
        account.ExpiresAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(5));
        (await broker.ObserveAsync(Token)).ValidUntil.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(5));
    }

    [Fact]
    public async Task Claude_renewal_extends_the_same_account_catalogue_in_place_without_network_and_refuses_when_stale()
    {
        using var clock = new Clock { Now = DateTimeOffset.UtcNow };
        var f = new ClaudeAccountBrokerTests.Fixture { Time = clock };
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var account = (await broker.ResolveAsync(profile, Token))!;
        var requests = f.Requests.Count;
        f.Audits.Clear();
        broker.IsLeaseCurrent(profile).Should().BeTrue();
        clock.Now += TimeSpan.FromMinutes(4.5);
        (await broker.RenewAsync(account, Token)).Should().BeTrue();
        account.Catalogue.ValidUntil.Should().Be(clock.Now.AddMinutes(5));
        account.Catalogue.Revision.Should().Be(catalogue.Revision);
        (await broker.RenewAsync(account, Token)).Should().BeFalse("nothing further can be extended yet");
        f.Requests.Should().HaveCount(requests, "renewal sends no content");
        f.Audits.Select(item => (item.ActionId, item.Outcome)).Should().Equal(
            ("model.claude.lease-renew", SecurityAuditOutcome.Requested), ("model.claude.lease-renew", SecurityAuditOutcome.Succeeded));
        clock.Now += TimeSpan.FromMinutes(4.5);
        broker.IsLeaseCurrent(profile).Should().BeTrue();
        (await broker.ResolveAsync(profile, Token)).Should().BeSameAs(account);
        clock.Now += TimeSpan.FromMinutes(1);
        broker.IsLeaseCurrent(profile).Should().BeFalse();
        (await broker.RenewAsync(account, Token)).Should().BeFalse("an expired lease is never resurrected");
        f.Approved = false;
        (await broker.RenewAsync(account, Token)).Should().BeFalse();
    }

    [Fact]
    public async Task Renewal_loop_renews_before_expiry_stops_on_refusal_reports_failure_and_joins_on_dispose()
    {
        using var clock = new Clock { Now = DateTimeOffset.UnixEpoch };
        var lifetime = new ModelTurnLifetime(HostRequest.Create(RequestOrigin.LocalUi), clock);
        var expiresAt = DateTimeOffset.UnixEpoch.AddMinutes(5);
        var renewals = 0;
        using var renewed = new SemaphoreSlim(0);
        var failures = new List<Exception>();
        HostedLeaseRenewal.Start(null, () => expiresAt, _ => ValueTask.FromResult(true), clock, failures.Add, Token)
            .Should().BeNull("work not owned by a host turn keeps its fixed lease");
        await using (HostedLeaseRenewal.Start(lifetime, () => expiresAt, _ =>
        {
            renewals++;
            expiresAt = clock.Now.AddMinutes(5);
            renewed.Release();
            return ValueTask.FromResult(renewals < 2);
        }, clock, failures.Add, Token))
        {
            await clock.WaitForTimers(1);
            clock.Timers[0].Due.Should().Be(TimeSpan.FromMinutes(4), "renewal starts one minute before expiry");
            clock.Advance(TimeSpan.FromMinutes(4));
            (await renewed.WaitAsync(TimeSpan.FromSeconds(5), Token)).Should().BeTrue();
            await clock.WaitForTimers(2);
            clock.Timers[1].Due.Should().Be(TimeSpan.FromMinutes(4));
            clock.Advance(TimeSpan.FromMinutes(4));
            (await renewed.WaitAsync(TimeSpan.FromSeconds(5), Token)).Should().BeTrue();
        }
        renewals.Should().Be(2, "a refused renewal ends the loop and the lease lapses normally");
        clock.Timers.Should().HaveCount(2);
        failures.Should().BeEmpty();
        await using (HostedLeaseRenewal.Start(lifetime, () => clock.Now, _ => throw new IOException("Synthetic renewal fault"),
            clock, failures.Add, Token)) { }
        failures.Should().ContainSingle().Which.Should().BeOfType<IOException>();
        var waiting = HostedLeaseRenewal.Start(lifetime, () => clock.Now.AddHours(1), _ => ValueTask.FromResult(true), clock,
            failures.Add, Token)!;
        await clock.WaitForTimers(3);
        await waiting.DisposeAsync();
        failures.Should().ContainSingle("disposal cancels a pending wait without reporting a failure");
    }

    [Fact]
    public async Task Claude_check_in_turn_outlives_its_context_window_during_long_reviews_with_the_unbounded_sdk_client()
    {
        using var clock = new Clock { Now = DateTimeOffset.UtcNow };
        var f = new ClaudeAccountBrokerTests.Fixture { Time = clock };
        await using var broker = f.Create();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var lifetime = new ModelTurnLifetime(request, clock);
        lifetime.UseCheckIns();
        using var turn = ModelTurnLifetime.Enter(lifetime);
        var context = new ModelContextEnvelope(request, "trusted policy", "body", [],
            clock.Now, clock.Now.AddMinutes(1), ModelToolMode.None);
        f.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(
            message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
                ? "{\"input_tokens\":25}" : ClaudeMessagesRuntimeTests.Answer);
        await using var runtime = new ClaudeMessagesRuntime(broker, clock, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var result = await runtime.RunAsync(new(new(Guid.NewGuid()), request, catalogue.Models[0].Selection), f.Profile(catalogue),
            context, (_, _) =>
            {
                // Each final-byte review takes two minutes: together they pass the one-minute context window.
                clock.Now += TimeSpan.FromMinutes(2);
                return ValueTask.FromResult(true);
            }, _ => ValueTask.FromResult(true), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Answer.Should().Be("genuine exact answer");
        runtime.IsQuiescent.Should().BeTrue();
    }

    private sealed class Clock : TimeProvider, IDisposable
    {
        private readonly Lock gate = new();
        private readonly SemaphoreSlim created = new(0);
        private readonly List<ManualTimer> timers = [];
        internal DateTimeOffset Now { get; set; }
        internal IReadOnlyList<ManualTimer> Timers { get { lock (gate) { return [.. timers]; } } }
        public override DateTimeOffset GetUtcNow() => Now;
        public void Dispose() => created.Dispose();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state) { Due = dueTime };
            lock (gate) { timers.Add(timer); }
            created.Release();
            return timer;
        }
        internal async Task WaitForTimers(int count)
        {
            while (Timers.Count < count) { await created.WaitAsync(TimeSpan.FromSeconds(5), Token); }
        }
        internal void Advance(TimeSpan amount)
        {
            Now += amount;
            foreach (var timer in Timers.Where(timer => timer.Due >= TimeSpan.Zero && timer.Due <= amount))
            {
                timer.Due = Timeout.InfiniteTimeSpan;
                timer.Fire();
            }
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        internal TimeSpan Due { get; set; }
        internal void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return true; }
        public void Dispose() => Due = Timeout.InfiniteTimeSpan;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}