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
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class OutputDeviceConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OutputDeviceConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Durable_session_exact_duplicate_names_saved_reset_and_restart_never_substitute_or_play()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get(0).Source.Should().Be("unavailable");
        await fixture.Refresh();
        fixture.Service.Observe(fixture.Snapshot);
        var initial = fixture.Service.Get(0);
        initial.Source.Should().Be("default");
        initial.Desired.Should().Be("system-default");
        initial.Effective.Should().Be("one");
        fixture.Service.Choices.Should().HaveCount(3);
        fixture.Service.Choices[1].Label.Should().Be("Same [one]");
        fixture.Store.Authority!.IsActive.Should().BeTrue();
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        await fixture.Select("two");
        fixture.Preferences.Id.Should().Be("two");
        fixture.Service.Get(0).Source.Should().Be("saved");
        fixture.Service.Get(0).Effective.Should().Be("two");
        var oldChoice = fixture.Service.Choices[1];
        fixture.Snapshot = new([new("one", "Same")], new("one", "Same"));
        fixture.Service.Observe(fixture.Snapshot);
        fixture.Service.Get(0).Desired.Should().Be("two");
        fixture.Service.Get(0).Available.Should().BeFalse();
        fixture.Service.Get(0).Recovery.Should().Contain("no endpoint");
        var stale = () => fixture.Apply(oldChoice);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        await fixture.Refresh();
        await fixture.Select("system-default");
        fixture.Preferences.Id.Should().BeNull();
        fixture.Snapshot = new([], null);
        await fixture.Refresh();
        fixture.Service.Get(0).SystemDefault.Should().BeNull();
        fixture.Service.Get(0).Effective.Should().BeNull();
        await fixture.Select("system-default");
        fixture.Service.Get(0).Desired.Should().Be("system-default");
        fixture.Service.Get(0).Source.Should().Be("default");
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair =>
            pair[0].Outcome == SecurityAuditOutcome.Requested && pair[1].Outcome == SecurityAuditOutcome.Succeeded);
        fixture.Playback.InvalidationCalls.Should().BeGreaterThan(0);
        fixture.Playback.SpeechCalls.Should().Be(0);
        notifications.Should().BeGreaterThan(0);
        var restarted = fixture.CreateService();
        restarted.Observe(fixture.Snapshot);
        restarted.Get(0).Source.Should().Be("default");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("equal")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("inactive")]
    [InlineData("host")]
    [InlineData("metadata")]
    [InlineData("default")]
    public async Task Foreign_lookalike_stale_cross_session_or_changed_default_choices_are_not_authority(string kind)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var choice = fixture.Service.Choices[1];
        if (kind is "owner")
        {
            var foreign = fixture.CreateService();
            await foreign.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
            choice = foreign.Choices[1];
        }
        if (kind is "equal") { choice = new(choice.Device, choice.Revision, choice.Owner, choice.Session, choice.Origin, choice.RemainsAdmitted); }
        if (kind is "session") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (kind is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (kind is "inactive") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
        if (kind is "host") { fixture.Eligible = false; }
        if (kind is "metadata") { fixture.Snapshot = new([new("one", "changed"), new("two", "Same")], new("one", "changed")); }
        if (kind is "default") { fixture.Snapshot = fixture.Snapshot with { Default = fixture.Snapshot.Devices[1] }; }
        var act = () => fixture.Apply(choice);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("invalidate")]
    public async Task Storage_audit_or_invalidation_failure_cannot_activate_or_claim_saved_output(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Preferences.Failure = stage switch { "io" => new IOException(), "access" => new UnauthorizedAccessException(), _ => null };
        fixture.Playback.InvalidateFailure = stage is "invalidate" ? new InvalidOperationException() : null;
        fixture.Audit.BeforeWrite = item =>
        {
            if (stage is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || stage is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("audit failed"); }
        };
        var act = () => fixture.Select("two");
        await act.Should().ThrowAsync<Exception>();
        fixture.Service.Get(0).Desired.Should().Be("system-default");
        if (stage is "terminal-audit")
        {
            fixture.Preferences.Id.Should().Be("two");
            fixture.Service.Get(0).Available.Should().BeFalse();
        }
        else { fixture.Preferences.Id.Should().BeNull(); }
        fixture.Audit.BeforeWrite = null;
        fixture.Preferences.Failure = null;
        fixture.Playback.InvalidateFailure = null;
        await fixture.Refresh();
        fixture.Service.Get(0).Available.Should().BeTrue();
    }

    [Fact]
    public async Task Protected_unknown_voice_stale_call_and_host_changes_are_denied_without_persistence()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Call.State = CallState.Unknown;
        fixture.Policy.LoadSettings(new(true, false));
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, static () => true, fixture.Token);
        var voice = await fixture.Service.SelectAsync(fixture.Service.Choices[1], fixture.Policy.Current.Revision,
            RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand, fixture.Policy, () => fixture.Eligible, fixture.Token);
        voice.Should().BeFalse();
        var invalidOrigin = () => fixture.Service.SelectAsync(fixture.Service.Choices[1], 0, RequestOrigin.LocalUi,
            SecurityAuditInitiator.VoiceCommand, fixture.Policy, static () => true, fixture.Token);
        await invalidOrigin.Should().ThrowAsync<InvalidOperationException>();
        await fixture.Refresh();
        var stale = await fixture.Service.SelectAsync(fixture.Service.Choices[1], 99, RequestOrigin.LocalUi,
            SecurityAuditInitiator.LocalUser, fixture.Policy, static () => true, fixture.Token);
        stale.Should().BeFalse();
        fixture.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Requested) { fixture.Eligible = false; } };
        (await fixture.Apply(fixture.Service.Choices[1])).Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Fact]
    public async Task Corrupt_saved_values_detection_failure_mute_and_hold_are_explicit_not_defaults()
    {
        await using var fixture = new Fixture();
        fixture.Preferences.LoadFailure = new InvalidDataException("corrupt");
        var load = fixture.Refresh;
        await load.Should().ThrowAsync<InvalidDataException>();
        fixture.Service.Get(0).Source.Should().Be("unavailable");
        fixture.Preferences.LoadFailure = null;
        fixture.Snapshot = new([new("one", "Same", IsMuted: true)], new("one", "Same", IsMuted: true));
        await fixture.Refresh();
        fixture.Service.Get(0).Muted.Should().BeTrue();
        fixture.Service.Get(0).Available.Should().BeFalse();
        fixture.Service.Get(0).Recovery.Should().Contain("Acoustic");
        fixture.Service.HoldUnavailable("metadata failed");
        fixture.Service.Get(0).Recovery.Should().Be("metadata failed");
        fixture.Service.Choices.Should().BeEmpty();
        fixture.Service.Observe(fixture.Snapshot);
        fixture.Service.Get(0).Effective.Should().BeNull();
        await fixture.Refresh();
        fixture.Service.Get(0).Effective.Should().Be("one");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("controls")]
    [InlineData("system")]
    [InlineData("duplicate")]
    [InlineData("default")]
    public async Task Unknown_or_inconsistent_metadata_fails_closed(string kind)
    {
        await using var fixture = new Fixture();
        fixture.Snapshot = kind switch
        {
            "empty" => new([new("", "Empty")], null),
            "controls" => new([new("id\n", "Control")], null),
            "system" => new([SystemAudioDevices.Output], null),
            "duplicate" => new([new("one", "Same"), new("one", "Same")], null),
            _ => new([new("one", "Same")], new("two", "Same")),
        };
        var act = fixture.Refresh;
        await act.Should().ThrowAsync<InvalidDataException>();
        var observe = () => fixture.Service.Observe(fixture.Snapshot);
        observe.Should().Throw<InvalidDataException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Cancelled_before_admission_and_after_atomic_commit_are_truthful_and_reentrant_choice_rejected()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => fixture.Service.SelectAsync(fixture.Service.Choices[1], 0, RequestOrigin.LocalUi,
            SecurityAuditInitiator.LocalUser, fixture.Policy, static () => true, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        fixture.Audit.Events.Should().BeEmpty();
        using var committed = new CancellationTokenSource();
        fixture.Preferences.AfterWrite = committed.Cancel;
        var old = fixture.Service.Choices[1];
        var saved = await fixture.Service.SelectAsync(old, 0, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
            fixture.Policy, static () => true, committed.Token);
        saved.Should().BeTrue();
        fixture.Preferences.Id.Should().Be("one");
    }

    [Fact]
    public async Task Changed_saved_preference_original_provenance_and_stale_presentation_context_require_fresh_discovery()
    {
        await using var fixture = new Fixture();
        var original = true;
        await fixture.Service.RefreshAsync(RequestOrigin.LocalUi, () => original, fixture.Token);
        var old = fixture.Service.Choices[1];
        original = false;
        var expired = () => fixture.Service.SelectAsync(old, 0, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            fixture.Policy, static () => true, fixture.Token);
        await expired.Should().ThrowAsync<InvalidOperationException>();
        await fixture.Service.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
        fixture.Preferences.Id = "two";
        var savedChanged = () => fixture.Select("one");
        await savedChanged.Should().ThrowAsync<InvalidOperationException>();
        await fixture.Refresh();
        var crossOrigin = () => fixture.Service.SelectAsync(fixture.Service.Choices[1], 0, RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.VoiceCommand, fixture.Policy, static () => true, fixture.Token);
        await crossOrigin.Should().ThrowAsync<InvalidOperationException>();
        fixture.Service.HoldUnavailable("receipt unavailable");
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Oversized_discovery_never_publishes_partial_choices_or_state()
    {
        await using var fixture = new Fixture();
        fixture.Snapshot = new([new("one", new string('x', 65536))], null);
        var oversized = fixture.Refresh;
        await oversized.Should().ThrowAsync<InvalidDataException>();
        fixture.Service.Choices.Should().BeEmpty();
        fixture.Service.Metadata.Should().BeNull();
        fixture.Service.Get(0).Source.Should().Be("unavailable");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Host_or_output_change_before_terminal_receipt_cannot_activate_newly_committed_state(bool apply, bool ownerChanged)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var notices = 0;
        fixture.Service.Changed += (_, _) => notices++;
        if (!apply) { fixture.Snapshot = new([new("one", "Same"), new("three", "New")], new("one", "Same")); }
        fixture.Store.BeforeCommit = record =>
        {
            if (record.State != HostTaskState.Succeeded) { return; }
            if (ownerChanged) { fixture.Eligible = false; }
            else { fixture.Service.HoldUnavailable("new output/privacy hold"); }
        };
        Func<Task> operation = apply ? async () => await fixture.Select("two") : fixture.Refresh;
        await operation.Should().ThrowAsync<InvalidOperationException>();
        fixture.Service.Get(0).Available.Should().BeFalse();
        fixture.Service.Choices.Should().BeEmpty();
        fixture.Preferences.Id.Should().Be(apply ? "two" : null);
        notices.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Reentrant_discovery_or_native_observation_cannot_replace_a_committing_snapshot()
    {
        await using var fixture = new Fixture();
        Task? reentrant = null;
        var observed = false;
        fixture.Service.Changed += (_, _) =>
        {
            if (observed) { return; }
            observed = true;
            reentrant = fixture.Refresh();
        };
        await fixture.Refresh();
        await reentrant!.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
        fixture.Audit.BeforeWrite = _ => fixture.Service.Observe(fixture.Snapshot);
        var observation = () => fixture.Select("two");
        await observation.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Service.Get(0).Available.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atomic_save_readback_failure_or_changed_normalization_never_activates_an_unconfirmed_pin(bool changed)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Preferences.AfterWrite = () =>
        {
            if (changed) { fixture.Preferences.Id = "normalized-different"; }
            else { fixture.Preferences.LoadFailure = new InvalidDataException("corrupt saved readback"); }
        };
        var apply = () => fixture.Select("two");
        await apply.Should().ThrowAsync<InvalidDataException>();
        fixture.Preferences.Writes.Should().Be(1);
        fixture.Service.Get(0).Source.Should().Be("unavailable");
        fixture.Service.Get(0).Available.Should().BeFalse();
        fixture.Service.Choices.Should().BeEmpty();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
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
        internal AudioOutputCatalogSnapshot Snapshot { get; set; } = new([new("one", "Same"), new("two", "Same")], new("one", "Same"));
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal Playback Playback { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy Policy { get; }
        internal OutputDeviceConfigurationService Service { get; }
        internal OutputDeviceConfigurationService CreateService() => new(Preferences,
            new BoundedAudioOutputCatalog(() => Task.FromResult(Snapshot), TimeProvider.System, NullLogger.Instance),
            Admission, Audit, Playback);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => Eligible, Token);
        internal Task<bool> Apply(OutputDeviceChoice choice) => Service.SelectAsync(choice, Policy.Current.Revision,
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, Policy, () => Eligible, Token);
        internal Task<bool> Select(string id) => Apply(Service.Choices.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal)));
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : IAudioDevicePreferences
    {
        internal string? Id { get; set; }
        internal Exception? Failure { get; set; }
        internal Exception? LoadFailure { get; set; }
        internal int Writes { get; private set; }
        internal Action? AfterWrite { get; set; }
        public string? LoadOutputDeviceId() { if (LoadFailure is { } failure) { throw failure; } return Id; }
        public void SaveOutputDeviceId(string outputDeviceId) { if (Failure is { } failure) { throw failure; } Id = outputDeviceId; Writes++; AfterWrite?.Invoke(); }
        public void ClearOutputDeviceId() { if (Failure is { } failure) { throw failure; } Id = null; Writes++; AfterWrite?.Invoke(); }
        public string? LoadMicrophoneId() => "retained";
        public void SaveMicrophoneId(string microphoneId) => throw new NotSupportedException();
        public void ClearMicrophoneId() => throw new NotSupportedException();
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent) { BeforeWrite?.Invoke(auditEvent); Events.Add(auditEvent); }
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
        internal int InvalidationCalls { get; private set; }
        internal int SpeechCalls { get; private set; }
        internal Exception? InvalidateFailure { get; set; }
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { if (InvalidateFailure is { } failure) { throw failure; } InvalidationCalls++; }
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default)
        { SpeechCalls++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
