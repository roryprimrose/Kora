using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ResponseModeConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ResponseModeConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Defaults_saved_restart_reset_and_narrower_precedence_remain_truthful_without_playback()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Observe();
        fixture.Service.Observe();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Service.Get().Saved.Should().BeNull();
        await fixture.Refresh();
        fixture.Service.Choices.Select(choice => choice.Label).Should().Equal("Hybrid", "VoiceOnly", "VisualOnly");
        var revision = fixture.Service.Get().Revision;
        await fixture.Select(ResponseOutputMode.VoiceOnly);
        fixture.Service.Get().Revision.Should().BeGreaterThan(revision);
        fixture.Service.Get().Source.Should().Be("saved");
        fixture.Service.Get(queueOverride: ResponseOutputMode.VisualOnly).Effective.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.Service.Get(queueOverride: ResponseOutputMode.VisualOnly, taskOverride: ResponseOutputMode.Hybrid).Effective.Should().Be(ResponseOutputMode.Hybrid);
        var restarted = fixture.CreateService();
        restarted.Observe();
        restarted.Get().Desired.Should().Be(ResponseOutputMode.VoiceOnly);
        await fixture.Refresh();
        await fixture.Select(ResponseOutputMode.Hybrid);
        fixture.Service.Get().Saved.Should().Be(ResponseOutputMode.Hybrid);
        fixture.Service.Get().Source.Should().Be("saved");
        fixture.Playback.Invalidations.Should().BeGreaterThan(0);
        fixture.Playback.SpeechCalls.Should().Be(0);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        fixture.Store.Tasks.Should().Contain(task => task.State == HostTaskState.Succeeded);
        fixture.Audit.Hosts.Should().OnlyContain(host => host != null && host.SessionId == fixture.Store.Authority!.SessionId);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("stale")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("inactive")]
    [InlineData("host")]
    [InlineData("channel")]
    [InlineData("saved")]
    [InlineData("hold")]
    [InlineData("disposed")]
    public async Task Unknown_foreign_stale_and_late_choices_cannot_mutate(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var choice = fixture.Service.Choices[1];
        if (stage is "foreign")
        {
            var other = fixture.CreateService();
            await other.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
            choice = other.Choices[1];
        }
        if (stage is "lookalike") { choice = new(choice.Mode, choice.Revision, choice.Owner, choice.Session, choice.Origin, choice.RemainsAdmitted); }
        if (stage is "stale") { await fixture.Refresh(); }
        if (stage is "session") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (stage is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (stage is "inactive") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
        if (stage is "host") { fixture.Eligible = false; }
        if (stage is "saved") { fixture.Preferences.Mode = ResponseOutputMode.VisualOnly; }
        if (stage is "hold") { fixture.Service.HoldUnavailable(); }
        if (stage is "disposed") { await fixture.Admission.DisposeAsync(); }
        var act = () => fixture.Apply(choice, stage is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("corrupt")]
    [InlineData("enum")]
    [InlineData("readback")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("receipt")]
    [InlineData("invalidate")]
    public async Task Atomic_readback_audit_or_receipt_failure_never_activates_unconfirmed_mode(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Preferences.SaveFailure = stage switch { "io" => new IOException(), "access" => new UnauthorizedAccessException(), _ => null };
        fixture.Preferences.AfterWrite = () =>
        {
            if (stage is "corrupt") { fixture.Preferences.LoadFailure = new InvalidDataException(); }
            if (stage is "enum") { fixture.Preferences.Mode = (ResponseOutputMode)100; }
            if (stage is "readback") { fixture.Preferences.Mode = ResponseOutputMode.Hybrid; }
        };
        fixture.Playback.Failure = stage is "invalidate" ? new InvalidOperationException() : null;
        fixture.Store.FailTerminal = stage is "receipt";
        fixture.Audit.BeforeWrite = item =>
        {
            if (stage is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || stage is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        var apply = () => fixture.Select(ResponseOutputMode.VoiceOnly);
        await apply.Should().ThrowAsync<Exception>();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Get().Source.Should().Be("unavailable");
        fixture.Service.Get().Effective.Should().BeNull();
        fixture.Service.Choices.Should().BeEmpty();
        fixture.Playback.Failure = null;
        fixture.Preferences.SaveFailure = null;
        fixture.Preferences.LoadFailure = null;
        fixture.Preferences.Pending = false;
        fixture.Preferences.Mode = ResponseOutputMode.VoiceOnly;
        fixture.Audit.BeforeWrite = null;
        fixture.Store.FailTerminal = false;
        if (stage is not "receipt") { await fixture.Refresh(); fixture.Service.Get().Available.Should().BeTrue(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_or_unreadable_storage_never_becomes_a_default(bool observe)
    {
        await using var fixture = new Fixture();
        fixture.Preferences.LoadFailure = new InvalidDataException("corrupt");
        Func<Task> read = observe ? () => { fixture.Service.Observe(); return Task.CompletedTask; } : fixture.Refresh;
        await read.Should().ThrowAsync<InvalidDataException>();
        fixture.Service.Get().Desired.Should().BeNull();
        fixture.Service.Get().Source.Should().Be("unavailable");
        fixture.Service.Choices.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Admission_or_revision_changes_before_terminal_receipt_fail_closed(bool apply, bool ownerChanged)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (ownerChanged) { fixture.Eligible = false; }
            else { fixture.Service.HoldUnavailable(); }
        };
        Func<Task> action = apply ? async () => await fixture.Select(ResponseOutputMode.VoiceOnly) : fixture.Refresh;
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Service.Get().Available.Should().BeFalse();
    }

    [Fact]
    public async Task Protected_voice_stale_call_ambient_channel_and_cancelled_mutations_never_write()
    {
        await using var fixture = new Fixture();
        fixture.Call.State = CallState.Unknown;
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => fixture.Eligible, fixture.Token);
        var denied = await fixture.Apply(fixture.Service.Choices[1], RequestOrigin.ActivatedVoice, fixture.Policy.Current.Revision,
            SecurityAuditInitiator.VoiceCommand);
        denied.Should().BeFalse();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        await fixture.Refresh();
        (await fixture.Apply(fixture.Service.Choices[1], callRevision: 99)).Should().BeFalse();
        var mislabelled = () => fixture.Apply(fixture.Service.Choices[1], initiator: SecurityAuditInitiator.VoiceCommand);
        await mislabelled.Should().ThrowAsync<InvalidOperationException>();
        var model = () => fixture.Apply(fixture.Service.Choices[1], initiator: SecurityAuditInitiator.ModelSuggestion);
        await model.Should().ThrowAsync<InvalidOperationException>();
        using (var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            var hostile = fixture.Refresh;
            await hostile.Should().ThrowAsync<InvalidOperationException>();
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancel = () => fixture.Service.SelectAsync(fixture.Service.Choices[1], fixture.Policy.Current.Revision,
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, fixture.Policy, static () => true, cancelled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Playback.SpeechCalls.Should().Be(0);
    }

    [Fact]
    public async Task Audit_race_rechecks_live_host_and_reentrant_observation_cannot_replace_commit()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Requested) { fixture.Eligible = false; } };
        (await fixture.Select(ResponseOutputMode.VoiceOnly)).Should().BeFalse();
        fixture.Eligible = true;
        fixture.Audit.BeforeWrite = null;
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = _ => fixture.Service.Observe();
        var observation = () => fixture.Select(ResponseOutputMode.VisualOnly);
        await observation.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_notification_reentrancy_cannot_acquire_or_replace_a_committing_snapshot()
    {
        await using var fixture = new Fixture();
        Task? reentrant = null;
        var notified = false;
        fixture.Service.Changed += (_, _) =>
        {
            if (notified) { return; }
            notified = true;
            reentrant = fixture.Refresh();
        };
        await fixture.Refresh();
        await reentrant!.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("late-readback")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-enum")]
    [InlineData("confirmed-io")]
    public async Task Evidence_confirmation_failure_retains_durable_unavailable_marker_even_after_restart(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Store.BeforeCommit = task =>
        {
            if (stage is "late-readback" && task.State == HostTaskState.Succeeded)
            {
                fixture.Preferences.Mode = ResponseOutputMode.VisualOnly;
            }
        };
        fixture.Preferences.ConfirmFailure = stage is "confirm" ? new IOException() : null;
        fixture.Preferences.AfterConfirm = () =>
        {
            if (stage is "confirmed-readback") { fixture.Preferences.Mode = ResponseOutputMode.Hybrid; }
            if (stage is "confirmed-enum") { fixture.Preferences.Mode = (ResponseOutputMode)100; }
            if (stage is "confirmed-io") { fixture.Preferences.LoadFailure = new IOException(); }
        };
        var apply = () => fixture.Select(ResponseOutputMode.VoiceOnly);
        await apply.Should().ThrowAsync<Exception>();
        fixture.Preferences.Pending.Should().BeTrue();
        fixture.Service.Get().Available.Should().BeFalse();
        var restarted = fixture.CreateService();
        var observe = restarted.Observe;
        observe.Should().Throw<InvalidDataException>();
        restarted.Get().Source.Should().Be("unavailable");
        restarted.Get().Desired.Should().BeNull();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Policy = new(Call);
            Service = CreateService();
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal Playback Playback { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy Policy { get; }
        internal ResponseModeConfigurationService Service { get; }
        internal ResponseModeConfigurationService CreateService() => new(Preferences, Admission, Audit, Playback);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => Eligible, Token);
        internal Task<bool> Select(ResponseOutputMode mode) => Apply(Service.Choices.Single(choice => choice.Mode == mode));
        internal Task<bool> Apply(ResponseModeChoice choice, RequestOrigin origin = RequestOrigin.LocalUi,
            long? callRevision = null, SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.SelectAsync(choice, callRevision ?? Policy.Current.Revision, origin, initiator, Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }
    private sealed class Preferences : IResponseOutputPreferences
    {
        internal bool Pending { get; set; }
        internal ResponseOutputMode? Mode { get; set; }
        internal Exception? LoadFailure { get; set; }
        internal Exception? SaveFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal int Writes { get; private set; }
        public ResponseOutputMode? LoadDefaultMode() => Pending ? throw new InvalidDataException("Unconfirmed write") : ReadBackDefaultMode();
        public ResponseOutputMode? ReadBackDefaultMode() { if (LoadFailure is { } error) { throw error; } return Mode; }
        public void BeginDefaultModeWrite() => Pending = true;
        public void ConfirmDefaultModeWrite()
        {
            if (ConfirmFailure is { } error) { throw error; }
            Pending = false;
            AfterConfirm?.Invoke();
        }
        public void SaveDefaultMode(ResponseOutputMode mode) { if (SaveFailure is { } error) { throw error; } Mode = mode; Writes++; AfterWrite?.Invoke(); }
        public bool? LoadMutedOutputVisualFallback() => false;
        public void SaveMutedOutputVisualFallback(bool enabled) => throw new NotSupportedException();
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest?> Hosts { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent) { BeforeWrite?.Invoke(auditEvent); Events.Add(auditEvent); Hosts.Add(HostActivity.Current?.Request); }
    }
    private sealed class Call : ICallStateService
    {
        private CallState state = CallState.Clear;
        internal CallState State { get => state; set { state = value; StateChanged?.Invoke(this, new(value)); } }
        public CallState CurrentState => State;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    }
    private sealed class Playback : ISpeechPlaybackService
    {
        internal int Invalidations { get; private set; }
        internal int SpeechCalls { get; private set; }
        internal Exception? Failure { get; set; }
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { if (Failure is { } error) { throw error; } Invalidations++; }
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default)
        { SpeechCalls++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
