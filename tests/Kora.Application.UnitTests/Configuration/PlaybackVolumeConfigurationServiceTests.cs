using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class PlaybackVolumeConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public PlaybackVolumeConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Genuine_audio_admission_atomic_audit_restart_reset_and_zero_have_no_playback_side_effects()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Available.Should().BeFalse();
        await fixture.Refresh();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Service.Observe();
        foreach (var percent in new[] { "0", "1", "100" })
        {
            (await fixture.Set(percent)).Should().BeTrue();
            fixture.Service.Get().Desired.Should().Be(PlaybackVolume.Parse(percent));
            fixture.Playback.Volume.Should().Be(PlaybackVolume.Parse(percent));
            fixture.Service.Get().AllowsSpeech.Should().Be(!string.Equals(percent, "0", StringComparison.Ordinal));
        }
        var restarted = fixture.CreateService();
        restarted.Observe();
        restarted.Get().Source.Should().Be("saved");
        fixture.Preferences.Value = null;
        restarted.Observe();
        restarted.Get().Source.Should().Be("default");
        fixture.Preferences.Value = PlaybackVolume.Default;
        (await fixture.Set(null)).Should().BeTrue();
        fixture.Preferences.Value.Should().BeNull();
        fixture.Playback.Volume.Should().Be(PlaybackVolume.Default);
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Playback.SpeechCalls.Should().Be(0);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair =>
            pair[0].Outcome == SecurityAuditOutcome.Requested && pair[1].Outcome == SecurityAuditOutcome.Succeeded);
        fixture.Store.Authority!.IsActive.Should().BeTrue();
        fixture.Audit.Requests.Should().OnlyContain(request => request.SessionId == fixture.Store.Authority.SessionId);
    }

    [Fact]
    public async Task Unsupported_gain_is_explicit_visual_unavailable_and_cannot_be_set()
    {
        await using var fixture = new Fixture(unsupported: true);
        await fixture.Refresh();
        fixture.Service.Observe();
        fixture.Service.Get().Desired.Should().Be(PlaybackVolume.Default);
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Get().Recovery.Should().Contain("no qualified");
        fixture.Service.Invoking(service => service.Propose("1", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("revision")]
    [InlineData("preference")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("inactive")]
    [InlineData("host")]
    [InlineData("captured-host")]
    [InlineData("origin")]
    public async Task Original_host_origin_current_session_generation_and_exact_held_proposal_are_authority(string hostile)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var proposal = fixture.Propose("30");
        var origin = RequestOrigin.LocalUi;
        if (hostile is "foreign")
        {
            var foreign = fixture.CreateService();
            await foreign.RefreshAsync(origin, () => fixture.Eligible, fixture.Token);
            proposal = foreign.Propose("30", foreign.Get().Revision, 0);
        }
        if (hostile is "lookalike") { proposal = new(proposal.Value, proposal.Revision, proposal.CallRevision, proposal.Session, proposal.Origin, proposal.Eligible); }
        if (hostile is "revision") { fixture.Service.HoldUnavailable(); await fixture.Refresh(); }
        if (hostile is "preference") { fixture.Preferences.Value = new(99); }
        if (hostile is "session") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (hostile is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (hostile is "inactive") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
        if (hostile is "host") { fixture.Eligible = false; }
        if (hostile is "captured-host") { fixture.CapturedEligible = false; }
        if (hostile is "origin") { origin = RequestOrigin.ActivatedVoice; }
        var act = () => fixture.Apply(proposal, origin);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Service.Get().Source.Should().Be("unavailable");
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
    [InlineData("invalidate")]
    public async Task Persistence_readback_audit_cancellation_or_evidence_failures_never_activate_unconfirmed_gain(string stage)
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
        if (stage is "terminal-change" or "terminal-null")
        {
            fixture.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.Preferences.Value = stage is "terminal-null" ? null : new PlaybackVolume(2); }
            };
        }
        if (stage is "invalidate") { fixture.Playback.Failure = new InvalidOperationException(); }
        if (stage is "begin-write") { fixture.Preferences.BeginFailure = new IOException(); }
        if (stage is "confirm-write") { fixture.Preferences.ConfirmFailure = new IOException(); }
        if (stage is "confirmed-read" or "confirmed-access" or "confirmed-change" or "confirmed-null")
        {
            fixture.Preferences.AfterConfirm = () =>
            {
                if (stage is "confirmed-read") { fixture.Preferences.ReadFailure = new IOException(); }
                else if (stage is "confirmed-access") { fixture.Preferences.ReadFailure = new UnauthorizedAccessException(); }
                else { fixture.Preferences.Value = stage is "confirmed-null" ? null : new PlaybackVolume(2); }
            };
        }
        var act = () => fixture.Apply(proposal);
        await act.Should().ThrowAsync<Exception>();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Get().Recovery.Should().Contain("unconfirmed");
        fixture.Playback.SpeechCalls.Should().Be(0);
        if (fixture.Preferences.Pending)
        {
            fixture.Preferences.ReadFailure = null;
            fixture.CreateService().Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public async Task Unknown_or_protected_calls_and_stale_call_revision_deny_original_voice_without_changing_gain()
    {
        await using var fixture = new Fixture();
        fixture.Call.State = CallState.Unknown;
        fixture.Policy.LoadSettings(new(true, false));
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => fixture.Eligible, fixture.Token);
        var voice = fixture.Propose("0");
        (await fixture.Apply(voice, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        fixture.Service.Get().Effective.Should().Be(PlaybackVolume.Default);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        fixture.Preferences.Writes.Should().Be(0);
        await fixture.Refresh();
        var stale = fixture.Service.Propose("0", fixture.Service.Get().Revision, 99);
        (await fixture.Apply(stale)).Should().BeFalse();
        var mismatch = () => fixture.Apply(stale, RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand);
        await mismatch.Should().ThrowAsync<InvalidOperationException>();
        var invalidInitiator = () => fixture.Apply(stale, RequestOrigin.LocalUi, (SecurityAuditInitiator)999);
        await invalidInitiator.Should().ThrowAsync<InvalidOperationException>();
        fixture.Call.State = CallState.Active;
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => fixture.Eligible, fixture.Token);
        (await fixture.Apply(fixture.Propose("0"), RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        await fixture.Refresh();
        (await fixture.Set("0")).Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_corruption_changed_state_and_receipt_failure_are_explicit_holds_and_require_fresh_admission()
    {
        await using var fixture = new Fixture();
        fixture.Preferences.ReadFailure = new InvalidDataException();
        var failed = fixture.Refresh;
        await failed.Should().ThrowAsync<InvalidDataException>();
        fixture.Service.Get().Source.Should().Be("unavailable");
        fixture.Preferences.ReadFailure = null;
        await fixture.Refresh();
        fixture.CapturedEligible = false;
        await fixture.Service.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
        fixture.CapturedEligible = true;
        fixture.Store.BeforeCommit = record =>
        {
            if (record.IsTerminal) { fixture.Preferences.Value = new(1); }
        };
        await failed.Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.BeforeCommit = null;
        await fixture.Refresh();
        fixture.Store.BeforeCommit = record => { if (record.IsTerminal) { fixture.CapturedEligible = false; } };
        await failed.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cancellation_before_commit_and_inside_policy_never_save_and_post_commit_does_not_invent_unsaved_outcome()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var proposal = fixture.Propose("1");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var act = () => fixture.Service.ApplyAsync(proposal, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            fixture.Policy, () => fixture.Eligible, cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Preferences.Writes.Should().Be(0);
        await fixture.Refresh();
        proposal = fixture.Propose("1");
        using var inside = new CancellationTokenSource();
        fixture.Audit.BeforeWrite = _ => inside.Cancel();
        (await fixture.Service.ApplyAsync(proposal, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            fixture.Policy, () => fixture.Eligible, inside.Token)).Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.BeforeWrite = null;
        await fixture.Refresh();
        proposal = fixture.Propose("1");
        using var after = new CancellationTokenSource();
        fixture.Preferences.AfterWrite = after.Cancel;
        (await fixture.Service.ApplyAsync(proposal, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            fixture.Policy, () => fixture.Eligible, after.Token)).Should().BeTrue();
        fixture.Preferences.Value.Should().Be(new PlaybackVolume(1));
    }

    [Fact]
    public async Task Unadmitted_stale_invalid_and_reentrant_proposals_do_not_mutate_or_relabel_current_snapshot()
    {
        await using var fixture = new Fixture();
        fixture.Service.Invoking(service => service.Propose("0", 0, 0)).Should().Throw<InvalidOperationException>();
        await fixture.Refresh();
        fixture.Service.Invoking(service => service.Propose("01", service.Get().Revision, 0)).Should().Throw<ArgumentOutOfRangeException>();
        fixture.Service.Invoking(service => service.Propose("0", 99, 0)).Should().Throw<InvalidOperationException>();
        fixture.CapturedEligible = false;
        fixture.Service.Invoking(service => service.Propose("0", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        fixture.CapturedEligible = true;
        fixture.Audit.BeforeWrite = _ => fixture.Service.Observe();
        var reentrant = () => fixture.Set("0");
        await reentrant.Should().ThrowAsync<InvalidOperationException>();
        fixture.Audit.BeforeWrite = null;
        var notifications = 0;
        fixture.Service.Changed += (_, _) =>
        {
            notifications++;
            fixture.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>();
            fixture.Service.Invoking(service => service.Propose("1", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        };
        await fixture.Refresh();
        (await fixture.Set("0")).Should().BeTrue();
        notifications.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Requested_audit_revalidates_context_host_and_saved_proposal_before_commit()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = _ => fixture.CapturedEligible = false;
        (await fixture.Set("1")).Should().BeFalse();
        fixture.CapturedEligible = true;
        fixture.Audit.BeforeWrite = _ => fixture.Eligible = false;
        (await fixture.Set("1")).Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audit_callback_cannot_replace_saved_volume_at_commit_or_after_reset_receipt(bool reset)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == (reset ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Requested)) { fixture.Preferences.Value = new(99); }
        };
        var action = () => fixture.Set(reset ? null : "1");
        await action.Should().ThrowAsync<InvalidDataException>();
        fixture.Service.Get().Available.Should().BeFalse();
    }

    [Fact]
    public async Task Correlated_host_system_origin_cannot_be_relabelled_as_original_local_input()
    {
        await using var fixture = new Fixture();
        using var incoming = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        var refresh = fixture.Refresh;
        await refresh.Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Authority.Should().BeNull();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Service.Get().Available.Should().BeFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(bool unsupported = false)
        {
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Policy = new(Call);
            Service = new(Preferences, unsupported ? new UnsupportedPlayback() : Playback, Admission, Audit);
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal bool CapturedEligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal Playback Playback { get; } = new();
        internal Audit Audit { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy Policy { get; }
        internal PlaybackVolumeConfigurationService Service { get; }
        internal PlaybackVolumeConfigurationService CreateService() => new(Preferences, Playback, Admission, Audit);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => CapturedEligible, Token);
        internal PlaybackVolumeProposal Propose(string? value) => Service.Propose(value, Service.Get().Revision, Policy.Current.Revision);
        internal Task<bool> Set(string? value) => Apply(Propose(value));
        internal Task<bool> Apply(PlaybackVolumeProposal proposal, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.ApplyAsync(proposal, origin, initiator, Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : IPlaybackVolumePreferences
    {
        internal PlaybackVolume? Value { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal int Writes { get; private set; }
        internal Action? AfterWrite { get; set; }
        internal bool Pending { get; private set; }
        internal Exception? BeginFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterConfirm { get; set; }
        public PlaybackVolume? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed write"); }
            return ReadBack();
        }
        public PlaybackVolume? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() { if (BeginFailure is { } failure) { throw failure; } Pending = true; }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(PlaybackVolume volume) { if (WriteFailure is { } failure) { throw failure; } Value = volume; Writes++; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; Writes++; }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest> Requests { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            BeforeWrite?.Invoke(auditEvent);
            Requests.Add(HostActivity.RequireCurrent().Request);
            Events.Add(auditEvent);
        }
    }
    private sealed class Call : ICallStateService
    {
        private CallState state = CallState.Clear;
        internal CallState State { get => state; set { state = value; StateChanged?.Invoke(this, new(value)); } }
        public CallState CurrentState => State;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    }
    private class UnsupportedPlayback : ISpeechPlaybackService
    {
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public virtual void InvalidateOutput() { }
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No test may synthesize.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Playback : UnsupportedPlayback, IPlaybackVolumeControl
    {
        internal PlaybackVolume? Volume { get; private set; } = PlaybackVolume.Default;
        internal Exception? Failure { get; set; }
        internal int SpeechCalls => 0;
        public override void InvalidateOutput() { if (Failure is { } failure) { throw failure; } }
        public void SetPlaybackVolume(PlaybackVolume? volume) => Volume = volume;
    }
}
