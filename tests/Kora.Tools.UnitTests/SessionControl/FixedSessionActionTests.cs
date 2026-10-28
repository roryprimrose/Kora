using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Tools.SessionControl;

using Microsoft.Extensions.Logging;

namespace Kora.Tools.UnitTests.SessionControl;

[Collection("Host tracing")]
public sealed class FixedSessionActionTests
{
    [Theory]
    [InlineData(FixedSessionAction.Lock, RequestOrigin.LocalUi)]
    [InlineData(FixedSessionAction.Shutdown, RequestOrigin.ActivatedVoice)]
    [InlineData(FixedSessionAction.Restart, RequestOrigin.LocalUi)]
    public async Task Each_closed_action_requests_once_and_never_claims_physical_completion(
        FixedSessionAction action, RequestOrigin origin)
    {
        using var fixture = new Fixture(origin);
        fixture.Broker.CurrentIdentityDigest.Should().Be(fixture.Platform.CurrentIdentityDigest);
        var result = await fixture.Run(action, token: TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(FixedSessionEffectOutcome.RequestAccepted);
        result.AuditConfirmed.Should().BeTrue();
        fixture.Platform.Requests.Should().Equal(action);
        fixture.Audit.Events.Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Unknown);
        fixture.Audit.Events[1].ReasonCode.Should().Be("request-accepted-physical-state-unobserved");
        fixture.Audit.Events[0].Initiator.Should().Be(origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("identity")]
    [InlineData("authorization")]
    [InlineData("source-after-await")]
    [InlineData("identity-after-await")]
    public async Task Current_authority_is_required_after_await_and_before_the_only_effect(string fault)
    {
        using var fixture = new Fixture();
        if (fault is "source") { fixture.Current = false; }
        if (fault is "identity") { fixture.Platform.CurrentIdentityDigest = null; }
        var result = await fixture.Run(authorize: _ =>
        {
            if (fault is "source-after-await") { fixture.Current = false; }
            if (fault is "identity-after-await") { fixture.Platform.CurrentIdentityDigest = null; }
            return ValueTask.FromResult(fault is not "authorization");
        }, token: TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(FixedSessionEffectOutcome.Denied);
        fixture.Platform.Requests.Should().BeEmpty();
        fixture.Audit.Events.Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData(FixedSessionEffectOutcome.Denied, SecurityAuditOutcome.Denied)]
    [InlineData(FixedSessionEffectOutcome.Failed, SecurityAuditOutcome.Failed)]
    [InlineData(FixedSessionEffectOutcome.Unconfirmed, SecurityAuditOutcome.Unknown)]
    public async Task Native_failures_and_uncertainty_remain_truthful(
        FixedSessionEffectOutcome outcome, SecurityAuditOutcome auditOutcome)
    {
        using var fixture = new Fixture();
        fixture.Platform.Result = new(outcome, 5);
        var result = await fixture.Run(token: TestContext.Current.CancellationToken);
        result.Should().Be(fixture.Platform.Result);
        fixture.Platform.Requests.Should().ContainSingle();
        fixture.Audit.Events[1].Outcome.Should().Be(auditOutcome);
    }

    [Fact]
    public async Task Failed_requested_audit_prevents_any_native_call()
    {
        using var fixture = new Fixture();
        fixture.Audit.FailAt = 1;
        await fixture.Invoking(value => value.Run()).Should().ThrowAsync<IOException>();
        fixture.Platform.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Post_call_failure_is_typed_unconfirmed_without_retry(bool terminalAudit, bool logging)
    {
        using var fixture = new Fixture();
        fixture.Logger.Enabled = logging;
        if (terminalAudit) { fixture.Audit.FailAt = 2; }
        else { fixture.Platform.Throw = true; }
        var result = await fixture.Run(token: TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(FixedSessionEffectOutcome.Unconfirmed);
        result.AuditConfirmed.Should().Be(!terminalAudit);
        fixture.Platform.Requests.Should().ContainSingle();
        fixture.Logger.Messages.Should().HaveCount(logging ? 1 : 0);
    }

    [Fact]
    public async Task Unknown_actions_cannot_register_a_native_effect_or_write_an_accepted_audit()
    {
        using var fixture = new Fixture();
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, new(Guid.NewGuid()));
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Tool);
        Func<CancellationToken, ValueTask<bool>> permission = _ => ValueTask.FromResult(true);
        await ((Func<Task>)(async () => await fixture.Broker.RequestAsync((FixedSessionAction)99,
            new string('a', 64), permission, () => true, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => fixture.Broker.ExecuteAsync((FixedSessionAction)99,
            new string('a', 64), permission, () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        fixture.Platform.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_and_invalid_original_requests_have_zero_native_effects()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await fixture.Invoking(value => value.Run(token: cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        await fixture.Invoking(value => value.Run(authorize: null, useNullAuthorize: true)).Should().ThrowAsync<ArgumentNullException>();
        await fixture.Invoking(value => value.Run(useNullCurrent: true)).Should().ThrowAsync<ArgumentNullException>();
        fixture.Platform.Requests.Should().BeEmpty();
        using var host = new Fixture(RequestOrigin.HostSystem);
        await host.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>();
        using var root = new Fixture(invocation: false);
        await root.Invoking(value => value.Run()).Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        private readonly HostRequest request;
        internal Fixture(RequestOrigin origin = RequestOrigin.LocalUi, bool invocation = true)
        {
            ActivitySource.AddActivityListener(listener);
            request = new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), origin,
                invocation ? new(Guid.NewGuid()) : null);
            Broker = new(Platform, Audit, Logger);
        }
        internal Platform Platform { get; } = new();
        internal Audit Audit { get; } = new();
        internal FixedSessionActionBroker Broker { get; }
        internal CaptureLogger Logger { get; } = new();
        internal bool Current { get; set; } = true;
        internal async Task<FixedSessionEffectResult> Run(FixedSessionAction action = FixedSessionAction.Lock,
            Func<CancellationToken, ValueTask<bool>>? authorize = null,
            bool useNullAuthorize = false, bool useNullCurrent = false, CancellationToken token = default)
        {
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Tool);
            var permission = useNullAuthorize ? null! : authorize ?? (_ => ValueTask.FromResult(true));
            Func<bool> current = useNullCurrent ? null! : () => Current;
            return await Broker.ExecuteAsync(action, new string('a', 64), permission, current, token);
        }

        public void Dispose() => listener.Dispose();
    }

    private sealed class CaptureLogger : ILogger<FixedSessionActionBroker>
    {
        internal bool Enabled { get; set; }
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            logLevel.Should().Be(LogLevel.Error);
            exception.Should().NotBeNull();
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class Platform : IFixedSessionEffect
    {
        public string? CurrentIdentityDigest { get; set; } = new string('a', 64);
        internal List<FixedSessionAction> Requests { get; } = [];
        internal FixedSessionEffectResult Result { get; set; } = new(FixedSessionEffectOutcome.RequestAccepted);
        internal bool Throw { get; set; }
        public FixedSessionEffectResult Request(FixedSessionAction action)
        {
            Requests.Add(action);
            if (Throw) { throw new IOException("Synthetic native uncertainty."); }
            return Result;
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal int FailAt { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (Events.Count + 1 == FailAt) { throw new IOException("Synthetic required audit failure."); }
            Events.Add(auditEvent);
        }
    }
}