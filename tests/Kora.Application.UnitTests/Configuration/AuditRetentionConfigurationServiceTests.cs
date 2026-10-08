using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class AuditRetentionConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public AuditRetentionConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Exact_bounds_default_reset_and_restart_activate_only_after_old_policy_receipts()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Service.Get().Saved.Should().BeNull();
        fixture.Service.Observe();
        foreach (var value in new[] { "30", "365", "90", null })
        {
            var previous = fixture.Policy.Effective;
            fixture.Audit.BeforeWrite = _ => fixture.Policy.Effective.Should().Be(previous);
            fixture.Store.BeforeCommit = _ => fixture.Policy.Effective.Should().Be(previous);
            (await fixture.Set(value)).Should().BeTrue();
            fixture.Service.Get().Saved.Should().Be(value is null ? null : AuditRetentionDays.Parse(value));
            fixture.Policy.Effective.Should().Be(value is null ? AuditRetentionDays.Default : AuditRetentionDays.Parse(value));
            fixture.CreateService().Observe();
            fixture.Service.Get().Source.Should().Be(value is null ? "default" : "saved");
        }
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && string.Equals(pair[0].ActionId, "configuration.audit-retention", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("revision")]
    [InlineData("preference")]
    [InlineData("source-only")]
    [InlineData("policy")]
    [InlineData("generation")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("host")]
    [InlineData("captured-host")]
    [InlineData("origin")]
    public async Task Host_proposal_input_generation_saved_source_effective_policy_and_revisions_are_exact(string hostile)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var proposal = fixture.Propose("30");
        var origin = RequestOrigin.LocalUi;
        if (hostile is "foreign")
        {
            var other = fixture.CreateService();
            await other.RefreshAsync(origin, () => true, fixture.Token);
            proposal = other.Propose("30", other.Get().Revision, 0);
        }
        if (hostile is "lookalike") { proposal = new(proposal.Value, proposal.Revision, proposal.CallRevision, proposal.Session, proposal.Origin, proposal.Eligible); }
        if (hostile is "revision") { fixture.Service.HoldUnavailable(); await fixture.Refresh(); }
        if (hostile is "preference") { fixture.Preferences.Value = new(365); }
        if (hostile is "source-only") { fixture.Preferences.Value = AuditRetentionDays.Default; }
        if (hostile is "policy") { fixture.Policy.Activate(new(365)); }
        if (hostile is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (hostile is "inactive") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
        if (hostile is "session") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (hostile is "host") { fixture.Eligible = false; }
        if (hostile is "captured-host") { fixture.CapturedEligible = false; }
        if (hostile is "origin") { origin = RequestOrigin.ActivatedVoice; }
        var action = () => fixture.Apply(proposal, origin);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Policy.Effective.Should().BeNull();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("access")]
    [InlineData("invalid")]
    [InlineData("readback")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("receipt")]
    [InlineData("host-after-receipt")]
    [InlineData("terminal-change")]
    [InlineData("terminal-null")]
    [InlineData("begin-write")]
    [InlineData("confirm-write")]
    [InlineData("confirmed-read")]
    [InlineData("confirmed-access")]
    [InlineData("confirmed-change")]
    [InlineData("confirmed-null")]
    [InlineData("confirmed-host")]
    [InlineData("confirmed-captured-host")]
    [InlineData("confirmed-policy")]
    [InlineData("confirmed-policy-held")]
    [InlineData("confirmed-state-held")]
    [InlineData("confirmed-cancel")]
    public async Task Atomic_write_readback_audit_and_marker_failures_hold_without_new_or_restart_activation(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        using var cancellation = new CancellationTokenSource();
        var proposal = fixture.Propose("30");
        if (stage is "read") { fixture.Preferences.ReadFailure = new IOException(); }
        if (stage is "write") { fixture.Preferences.WriteFailure = new IOException(); }
        if (stage is "access") { fixture.Preferences.WriteFailure = new UnauthorizedAccessException(); }
        if (stage is "invalid") { fixture.Preferences.WriteFailure = new InvalidDataException(); }
        if (stage is "readback") { fixture.Preferences.AfterWrite = () => fixture.Preferences.Value = new(31); }
        if (stage is "requested-audit" or "terminal-audit")
        {
            fixture.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == (stage is "requested-audit" ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded)) { throw new IOException(); }
            };
        }
        if (stage is "receipt") { fixture.Store.FailTerminal = true; }
        if (stage is "host-after-receipt") { fixture.Store.BeforeCommit = record => { if (record.IsTerminal) { fixture.Eligible = false; } }; }
        if (stage is "terminal-change" or "terminal-null")
        {
            fixture.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.Preferences.Value = stage is "terminal-null" ? null : new(31); }
            };
        }
        if (stage is "begin-write") { fixture.Preferences.BeginFailure = new IOException(); }
        if (stage is "confirm-write") { fixture.Preferences.ConfirmFailure = new IOException(); }
        if (stage is "confirmed-read") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.ReadFailure = new IOException(); }
        if (stage is "confirmed-access") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.ReadFailure = new UnauthorizedAccessException(); }
        if (stage is "confirmed-change") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.Value = new(31); }
        if (stage is "confirmed-null") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.Value = null; }
        if (stage is "confirmed-host") { fixture.Preferences.AfterConfirm = () => fixture.Eligible = false; }
        if (stage is "confirmed-captured-host") { fixture.Preferences.AfterConfirm = () => fixture.CapturedEligible = false; }
        if (stage is "confirmed-policy") { fixture.Preferences.AfterConfirm = () => fixture.Policy.Activate(new(365)); }
        if (stage is "confirmed-policy-held") { fixture.Preferences.AfterConfirm = fixture.Policy.HoldUnavailable; }
        if (stage is "confirmed-state-held") { fixture.Preferences.AfterConfirm = fixture.Service.HoldUnavailable; }
        if (stage is "confirmed-cancel") { fixture.Preferences.AfterConfirm = cancellation.Cancel; }
        var action = () => fixture.Service.ApplyAsync(proposal, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            fixture.CallPolicy, () => fixture.Eligible, cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        fixture.Policy.Effective.Should().BeNull();
        fixture.Service.Get().Source.Should().Be("unavailable");
        fixture.Preferences.ReadFailure = null;
        if (fixture.Preferences.Pending) { fixture.CreateService().Invoking(service => service.Observe()).Should().Throw<InvalidDataException>(); }
        fixture.Policy.Effective.Should().BeNull();
    }

    [Fact]
    public async Task Discovery_and_reset_receipts_revalidate_exact_saved_values_and_host_without_success()
    {
        await using var fixture = new Fixture();
        fixture.Store.AfterOperation = () => fixture.CapturedEligible = false;
        var refresh = fixture.Refresh;
        await refresh.Should().ThrowAsync<InvalidOperationException>();
        fixture.CapturedEligible = true;
        fixture.Store.AfterOperation = () => fixture.Preferences.Value = new(365);
        await refresh.Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.AfterOperation = null;
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.Preferences.Value = new(30); } };
        var reset = () => fixture.Set(null);
        await reset.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task No_proposal_invalid_stale_held_policy_and_reentrant_callbacks_never_commit()
    {
        await using var fixture = new Fixture();
        fixture.Service.Invoking(service => service.Propose("30", 0, 0)).Should().Throw<InvalidOperationException>();
        await fixture.Refresh();
        fixture.Service.Invoking(service => service.Propose("29", service.Get().Revision, 0)).Should().Throw<ArgumentOutOfRangeException>();
        fixture.Service.Invoking(service => service.Propose("30", 999, 0)).Should().Throw<InvalidOperationException>();
        fixture.CapturedEligible = false;
        fixture.Service.Invoking(service => service.Propose("30", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        fixture.CapturedEligible = true;
        fixture.Policy.HoldUnavailable();
        fixture.Service.Invoking(service => service.Propose("30", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        fixture.Service.HoldUnavailable();
        fixture.Service.Invoking(service => service.Propose("30", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        fixture.Service.Observe();
        fixture.Audit.BeforeWrite = _ => fixture.Service.Observe();
        var apply = () => fixture.Set("30");
        await apply.Should().ThrowAsync<InvalidOperationException>();
        fixture.Audit.BeforeWrite = null;
        fixture.Service.Changed += (_, _) =>
        {
            fixture.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>();
            fixture.Service.Invoking(service => service.Propose("30", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        };
        await fixture.Refresh();
        (await fixture.Set("30")).Should().BeTrue();
    }

    [Fact]
    public async Task Requested_callback_changes_original_input_policy_source_and_protected_call_are_denied_or_held()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = _ => fixture.Eligible = false;
        (await fixture.Set("30")).Should().BeFalse();
        fixture.Eligible = true;
        fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Requested) { fixture.Policy.Activate(new(365)); } };
        (await fixture.Set("30")).Should().BeFalse();
        fixture.Service.Observe();
        fixture.Audit.BeforeWrite = _ => fixture.Preferences.Value = new(31);
        var changed = () => fixture.Set("30");
        await changed.Should().ThrowAsync<InvalidDataException>();
        fixture.Audit.BeforeWrite = null;
        fixture.Preferences.Value = null;
        await fixture.Refresh();
        var proposal = fixture.Propose("30");
        foreach (var initiator in new[] { SecurityAuditInitiator.System, SecurityAuditInitiator.VoiceCommand })
        {
            var invalid = () => fixture.Service.ApplyAsync(proposal, RequestOrigin.LocalUi, initiator, fixture.CallPolicy, () => true, fixture.Token);
            await invalid.Should().ThrowAsync<InvalidOperationException>();
        }
        fixture.Call.Set(CallState.Active);
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        (await fixture.Apply(fixture.Propose("30"), RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        using var hostile = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        var relabel = fixture.Refresh;
        await relabel.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Early_and_late_cancellation_or_disposal_never_return_activated_success()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        using var early = new CancellationTokenSource();
        early.Cancel();
        var cancelled = () => fixture.Service.ApplyAsync(fixture.Propose("30"), RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, fixture.CallPolicy, () => true, early.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        await fixture.Refresh();
        using var late = new CancellationTokenSource();
        fixture.Preferences.AfterWrite = late.Cancel;
        var lateCancelled = () => fixture.Service.ApplyAsync(fixture.Propose("30"), RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, fixture.CallPolicy, () => true, late.Token);
        await lateCancelled.Should().ThrowAsync<OperationCanceledException>();
        fixture.Preferences.Pending.Should().BeTrue();
        fixture.Policy.Effective.Should().BeNull();
        await fixture.Admission.DisposeAsync();
        var disposed = fixture.Refresh;
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            CallPolicy = new(Call);
            Service = CreateService();
            Service.Observe();
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal bool CapturedEligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal AuditRetentionPolicy Policy { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AuditRetentionAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy CallPolicy { get; }
        internal AuditRetentionConfigurationService Service { get; }
        internal AuditRetentionConfigurationService CreateService() => new(Preferences, Policy, Admission, Audit);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => CapturedEligible, Token);
        internal AuditRetentionProposal Propose(string? value) => Service.Propose(value, Service.Get().Revision, CallPolicy.Current.Revision);
        internal Task<bool> Set(string? value) => Apply(Propose(value));
        internal Task<bool> Apply(AuditRetentionProposal proposal, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.ApplyAsync(proposal, origin, initiator, CallPolicy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { CallPolicy.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : IAuditRetentionPreferences
    {
        internal AuditRetentionDays? Value { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal Exception? BeginFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal int Writes { get; private set; }
        internal bool Pending { get; private set; }
        public AuditRetentionDays? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed write"); }
            return ReadBack();
        }
        public AuditRetentionDays? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() { if (BeginFailure is { } failure) { throw failure; } Pending = true; }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(AuditRetentionDays value) { if (WriteFailure is { } failure) { throw failure; } Value = value; Writes++; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; Writes++; AfterWrite?.Invoke(); }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent item) { BeforeWrite?.Invoke(item); Events.Add(item); }
    }
    private sealed class Call : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        internal void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetManualCallActiveAsync(bool active) => Task.CompletedTask;
    }
}
