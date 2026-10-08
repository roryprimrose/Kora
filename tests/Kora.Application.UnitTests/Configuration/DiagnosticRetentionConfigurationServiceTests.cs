using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class DiagnosticRetentionConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public DiagnosticRetentionConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Original_admitted_atomic_future_policy_min_max_saved_restart_reset_has_no_cleanup_authority()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Available.Should().BeFalse();
        await fixture.Refresh();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Service.Observe();
        foreach (var days in new[] { "1", "365", "30" })
        {
            var old = fixture.Policy.Effective;
            fixture.Audit.BeforeWrite = _ => fixture.Policy.Effective.Should().Be(old);
            (await fixture.Set(days)).Should().BeTrue();
            fixture.Policy.Effective.Should().Be(DiagnosticRetentionDays.Parse(days));
        }
        fixture.Audit.BeforeWrite = null;
        fixture.CreateService().Observe();
        fixture.Service.Get().Source.Should().Be("saved");
        (await fixture.Set(null)).Should().BeTrue();
        fixture.Preferences.Value.Should().BeNull();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Policy.Effective.Should().Be(DiagnosticRetentionDays.Default);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].ActionId == "configuration.sqlite-diagnostic-retention");
        fixture.Store.Tasks.Where(record => record.IsTerminal).Should().OnlyContain(record => record.State == HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("revision")]
    [InlineData("preference")]
    [InlineData("generation")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("host")]
    [InlineData("captured-host")]
    [InlineData("origin")]
    public async Task Exact_host_held_original_channel_session_generation_and_revisions_are_revalidated(string hostile)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var proposal = fixture.Propose("1");
        var origin = RequestOrigin.LocalUi;
        if (hostile is "foreign")
        {
            var other = fixture.CreateService();
            await other.RefreshAsync(origin, () => true, fixture.Token);
            proposal = other.Propose("1", other.Get().Revision, 0);
        }
        if (hostile is "lookalike") { proposal = new(proposal.Value, proposal.Revision, proposal.CallRevision, proposal.Session, proposal.Origin, proposal.Eligible); }
        if (hostile is "revision") { fixture.Service.HoldUnavailable(); await fixture.Refresh(); }
        if (hostile is "preference") { fixture.Preferences.Value = new(365); }
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
    public async Task Failed_atomic_readback_required_audit_receipt_and_confirmation_never_activate_unconfirmed_policy(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var proposal = fixture.Propose("1");
        if (stage is "read") { fixture.Preferences.ReadFailure = new IOException(); }
        if (stage is "write") { fixture.Preferences.WriteFailure = new IOException(); }
        if (stage is "access") { fixture.Preferences.WriteFailure = new UnauthorizedAccessException(); }
        if (stage is "invalid") { fixture.Preferences.WriteFailure = new InvalidDataException(); }
        if (stage is "readback") { fixture.Preferences.AfterWrite = () => fixture.Preferences.Value = new(2); }
        if (stage is "requested-audit" or "terminal-audit")
        {
            fixture.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == (stage is "requested-audit" ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded)) { throw new IOException(); }
            };
        }
        if (stage is "receipt") { fixture.Store.FailTerminal = true; }
        if (stage is "host-after-receipt") { fixture.Store.BeforeCommit = record => { if (record.IsTerminal) { fixture.Eligible = false; } }; }
        if (stage is "terminal-change")
        {
            fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.Preferences.Value = new(2); } };
        }
        if (stage is "terminal-null")
        {
            fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.Preferences.Value = null; } };
        }
        if (stage is "begin-write") { fixture.Preferences.BeginFailure = new IOException(); }
        if (stage is "confirm-write") { fixture.Preferences.ConfirmFailure = new IOException(); }
        if (stage is "confirmed-read") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.ReadFailure = new IOException(); }
        if (stage is "confirmed-access") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.ReadFailure = new UnauthorizedAccessException(); }
        if (stage is "confirmed-change") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.Value = new(2); }
        if (stage is "confirmed-null") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.Value = null; }
        if (stage is "confirmed-host") { fixture.Preferences.AfterConfirm = () => fixture.Eligible = false; }
        if (stage is "confirmed-captured-host") { fixture.Preferences.AfterConfirm = () => fixture.CapturedEligible = false; }
        var action = () => fixture.Apply(proposal);
        await action.Should().ThrowAsync<Exception>();
        fixture.Policy.Effective.Should().BeNull();
        fixture.Service.Get().Source.Should().Be("unavailable");
        fixture.Preferences.ReadFailure = null;
        if (fixture.Preferences.Pending)
        {
            fixture.CreateService().Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public async Task Discovery_receipt_cannot_publish_changed_host_or_saved_values_and_reset_receipt_is_exact()
    {
        await using var fixture = new Fixture();
        fixture.Store.AfterOperation = () => fixture.CapturedEligible = false;
        var hostChanged = fixture.Refresh;
        await hostChanged.Should().ThrowAsync<InvalidOperationException>();
        fixture.CapturedEligible = true;
        fixture.Store.AfterOperation = () => fixture.Preferences.Value = new(365);
        await hostChanged.Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.AfterOperation = null;
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.Preferences.Value = new(1); } };
        var resetChanged = () => fixture.Set(null);
        await resetChanged.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task No_proposal_invalid_stale_eligibility_and_reentrancy_never_commit()
    {
        await using var fixture = new Fixture();
        fixture.Service.Invoking(service => service.Propose("1", 0, 0)).Should().Throw<InvalidOperationException>();
        await fixture.Refresh();
        fixture.Service.Invoking(service => service.Propose("0", service.Get().Revision, 0)).Should().Throw<ArgumentOutOfRangeException>();
        fixture.Service.Invoking(service => service.Propose("1", 999, 0)).Should().Throw<InvalidOperationException>();
        fixture.CapturedEligible = false;
        fixture.Service.Invoking(service => service.Propose("1", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        fixture.CapturedEligible = true;
        fixture.Audit.BeforeWrite = _ => fixture.Service.Observe();
        var apply = () => fixture.Set("1");
        await apply.Should().ThrowAsync<InvalidOperationException>();
        fixture.Audit.BeforeWrite = null;
        fixture.Service.Changed += (_, _) =>
        {
            fixture.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>();
            fixture.Service.Invoking(service => service.Propose("1", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        };
        await fixture.Refresh();
        (await fixture.Set("1")).Should().BeTrue();
    }

    [Fact]
    public async Task Audit_callback_changes_denials_origin_and_protected_call_never_relabel_input()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = _ => fixture.Eligible = false;
        (await fixture.Set("1")).Should().BeFalse();
        fixture.Eligible = true;
        fixture.Audit.BeforeWrite = _ => fixture.Preferences.Value = new(2);
        var changed = () => fixture.Set("1");
        await changed.Should().ThrowAsync<InvalidDataException>();
        fixture.Audit.BeforeWrite = null;
        fixture.Preferences.Value = null;
        fixture.CapturedEligible = true;
        await fixture.Refresh();
        var proposal = fixture.Propose("1");
        foreach (var initiator in new[] { SecurityAuditInitiator.System, SecurityAuditInitiator.VoiceCommand })
        {
            var invalid = () => fixture.Service.ApplyAsync(proposal, RequestOrigin.LocalUi, initiator, fixture.CallPolicy, () => true, fixture.Token);
            await invalid.Should().ThrowAsync<InvalidOperationException>();
        }
        fixture.Call.Set(CallState.Active);
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        var voice = fixture.Propose("1");
        (await fixture.Apply(voice, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        using var untrusted = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        var relabel = fixture.Refresh;
        await relabel.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cancellation_late_receipt_disposal_and_queued_refresh_are_isolated()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => fixture.Service.ApplyAsync(fixture.Propose("1"), RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, fixture.CallPolicy, () => true, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        await fixture.Refresh();
        using var late = new CancellationTokenSource();
        fixture.Preferences.AfterWrite = late.Cancel;
        (await fixture.Service.ApplyAsync(fixture.Propose("1"), RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, fixture.CallPolicy, () => true, late.Token)).Should().BeTrue();
        await fixture.Admission.DisposeAsync();
        var disposed = fixture.Refresh;
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
        fixture.Policy.Effective.Should().BeNull();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            CallPolicy = new(Call);
            Service = CreateService();
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal bool CapturedEligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal DiagnosticRetentionPolicy Policy { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal DiagnosticRetentionAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy CallPolicy { get; }
        internal DiagnosticRetentionConfigurationService Service { get; }
        internal DiagnosticRetentionConfigurationService CreateService() => new(Preferences, Policy, Admission, Audit);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => CapturedEligible, Token);
        internal DiagnosticRetentionProposal Propose(string? value) => Service.Propose(value, Service.Get().Revision, CallPolicy.Current.Revision);
        internal Task<bool> Set(string? value) => Apply(Propose(value));
        internal Task<bool> Apply(DiagnosticRetentionProposal proposal, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.ApplyAsync(proposal, origin, initiator, CallPolicy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { CallPolicy.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : IDiagnosticRetentionPreferences
    {
        internal DiagnosticRetentionDays? Value { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal Exception? BeginFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal int Writes { get; private set; }
        internal bool Pending { get; private set; }
        public DiagnosticRetentionDays? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed write"); }
            return ReadBack();
        }
        public DiagnosticRetentionDays? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() { if (BeginFailure is { } failure) { throw failure; } Pending = true; }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(DiagnosticRetentionDays value) { if (WriteFailure is { } failure) { throw failure; } Value = value; Writes++; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; Writes++; AfterWrite?.Invoke(); }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent value) { BeforeWrite?.Invoke(value); HostActivity.RequireCurrent(); Events.Add(value); }
    }
    private sealed class Call : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        internal void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
    }
}
