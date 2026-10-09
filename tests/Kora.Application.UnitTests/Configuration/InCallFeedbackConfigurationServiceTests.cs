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
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class InCallFeedbackConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public InCallFeedbackConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Defaults_all_choices_restart_and_reset_have_truthful_correlated_receipts_without_speech()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Observe();
        fixture.Service.Observe();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Service.Get().Desired.Should().Be(InCallFeedbackMode.UI);
        fixture.Service.Get().Saved.Should().BeNull();
        foreach (var mode in Enum.GetValues<InCallFeedbackMode>())
        {
            await fixture.Refresh();
            fixture.Service.Choices.Select(choice => choice.Label).Should().Equal("Voice", "UI", "Both", "Inherit");
            (await fixture.Select(mode)).Should().BeTrue();
            fixture.Service.Get().Saved.Should().Be(mode);
            fixture.Service.Get().Source.Should().Be("saved");
            var restarted = fixture.CreateService();
            restarted.Observe();
            restarted.Get().Desired.Should().Be(mode);
        }
        await fixture.Refresh();
        (await fixture.Select(InCallFeedbackMode.UI, reset: true)).Should().BeTrue();
        fixture.Service.Get().Saved.Should().BeNull();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Service.Get().Desired.Should().Be(InCallFeedbackMode.UI);
        fixture.Playback.Invalidations.Should().BeGreaterThan(0);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        fixture.Audit.Hosts.Should().OnlyContain(host => host != null && host.SessionId == fixture.Store.Authority!.SessionId);
        fixture.Audit.Events.Select(item => item.CorrelationId).Should().Equal(fixture.Audit.Hosts.Select(host => host!.RequestId.Value));
        fixture.Store.Tasks.Should().Contain(task => task.State == HostTaskState.Succeeded);
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
    [InlineData("reset-target")]
    [InlineData("call-choice")]
    public async Task Foreign_stale_unknown_or_relabelled_choices_cannot_mutate(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var choice = fixture.Service.Choices[0];
        if (stage is "foreign")
        {
            var other = fixture.CreateService();
            await other.RefreshAsync(RequestOrigin.LocalUi, 0, static () => true, fixture.Token);
            choice = other.Choices[0];
        }
        if (stage is "lookalike") { choice = new(choice.Mode, choice.Revision, choice.CallRevision, choice.Session, choice.Origin, choice.Eligible); }
        if (stage is "stale") { await fixture.Refresh(); }
        if (stage is "session") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (stage is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (stage is "inactive") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
        if (stage is "host") { fixture.Eligible = false; }
        if (stage is "saved") { fixture.Preferences.Mode = InCallFeedbackMode.Both; }
        if (stage is "hold") { fixture.Service.HoldUnavailable(); }
        if (stage is "disposed") { await fixture.Admission.DisposeAsync(); }
        var action = () => fixture.Apply(choice, stage is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            stage is "call-choice" ? 99 : null, reset: stage is "reset-target");
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    [InlineData((CallState)99)]
    public async Task Protected_original_voice_cannot_mutate_but_fresh_ui_can_without_changing_call_protections(CallState state)
    {
        await using var fixture = new Fixture();
        fixture.Call.State = state;
        var observation = fixture.Policy.Current;
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, observation.Revision, () => fixture.Eligible, fixture.Token);
        (await fixture.Apply(fixture.Service.Choices[0], RequestOrigin.ActivatedVoice, initiator: SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        fixture.Preferences.Writes.Should().Be(0);
        await fixture.Refresh();
        await fixture.Select(InCallFeedbackMode.Voice);
        fixture.Policy.Current.Should().Be(observation);
        fixture.Policy.Current.SuppressSpeech.Should().BeTrue();
        fixture.Policy.Current.Authorization(true, true).AllowsReusableGrants.Should().BeFalse();
    }

    [Theory]
    [InlineData(CallState.Clear)]
    [InlineData(CallState.Unavailable)]
    public async Task Clear_or_unconfigured_detector_allows_exact_voice_configuration(CallState state)
    {
        await using var fixture = new Fixture();
        fixture.Call.State = state;
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, fixture.Policy.Current.Revision, () => fixture.Eligible, fixture.Token);
        (await fixture.Apply(fixture.Service.Choices[2], RequestOrigin.ActivatedVoice, initiator: SecurityAuditInitiator.VoiceCommand)).Should().BeTrue();
        fixture.Preferences.Mode.Should().Be(InCallFeedbackMode.Both);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("other")]
    [InlineData("enum")]
    [InlineData("readback")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("receipt")]
    [InlineData("invalidate")]
    [InlineData("begin")]
    [InlineData("reset")]
    public async Task Save_readback_audit_or_receipt_failure_never_activates_unconfirmed_state(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Preferences.SaveFailure = stage switch
        {
            "io" or "reset" => new IOException(),
            "access" => new UnauthorizedAccessException(),
            "other" => new InvalidOperationException(),
            _ => null,
        };
        fixture.Preferences.BeginFailure = stage is "begin" ? new IOException() : null;
        fixture.Preferences.AfterWrite = () =>
        {
            if (stage is "enum") { fixture.Preferences.Mode = (InCallFeedbackMode)99; }
            if (stage is "readback") { fixture.Preferences.Mode = InCallFeedbackMode.Inherit; }
        };
        fixture.Playback.Failure = stage is "invalidate" ? new InvalidOperationException() : null;
        fixture.Store.FailTerminal = stage is "receipt";
        fixture.Audit.BeforeWrite = item =>
        {
            if (stage is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || stage is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        await FluentActions.Awaiting(() => fixture.Select(stage is "reset" ? InCallFeedbackMode.UI : InCallFeedbackMode.Voice,
            reset: stage is "reset")).Should().ThrowAsync<Exception>();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Get().Source.Should().Be("unavailable");
        fixture.Service.Choices.Should().BeEmpty();
        if (stage is "terminal-audit" or "receipt" or "enum" or "readback" or "io" or "access" or "other" or "reset")
        {
            fixture.Preferences.Pending.Should().BeTrue();
            FluentActions.Invoking(() => fixture.CreateService().Observe()).Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData("late-readback")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-enum")]
    [InlineData("confirmed-io")]
    [InlineData("confirmed-host")]
    [InlineData("confirmed-call")]
    [InlineData("confirmed-cancel")]
    public async Task Confirmation_failure_retains_durable_marker_across_restart(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        using var cancelled = new CancellationTokenSource();
        fixture.Store.BeforeCommit = task =>
        {
            if (stage is "late-readback" && task.State == HostTaskState.Succeeded) { fixture.Preferences.Mode = InCallFeedbackMode.Both; }
        };
        fixture.Preferences.ConfirmFailure = stage is "confirm" ? new IOException() : null;
        fixture.Preferences.AfterConfirm = () =>
        {
            if (stage is "confirmed-readback") { fixture.Preferences.Mode = InCallFeedbackMode.Both; }
            if (stage is "confirmed-enum") { fixture.Preferences.Mode = (InCallFeedbackMode)99; }
            if (stage is "confirmed-io") { fixture.Preferences.ReadFailure = new IOException(); }
            if (stage is "confirmed-host") { fixture.Eligible = false; }
            if (stage is "confirmed-call") { fixture.Call.State = CallState.Active; }
            if (stage is "confirmed-cancel") { cancelled.Cancel(); }
        };
        await FluentActions.Awaiting(() => fixture.Service.SelectAsync(fixture.Service.Choices[0], false, fixture.Policy.Current.Revision,
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, fixture.Policy, () => fixture.Eligible, cancelled.Token))
            .Should().ThrowAsync<Exception>();
        fixture.Preferences.Pending.Should().BeTrue();
        fixture.Service.Get().Available.Should().BeFalse();
        FluentActions.Invoking(() => fixture.CreateService().Observe()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false, "host")]
    [InlineData(false, "revision")]
    [InlineData(false, "saved")]
    [InlineData(false, "cancel")]
    [InlineData(true, "host")]
    [InlineData(true, "revision")]
    [InlineData(true, "call")]
    [InlineData(true, "cancel")]
    public async Task Discovery_or_apply_receipt_races_fail_closed(bool apply, string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        using var cancelled = new CancellationTokenSource();
        fixture.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (stage is "host") { fixture.Eligible = false; }
            if (stage is "revision") { fixture.Service.HoldUnavailable(); }
            if (stage is "saved") { fixture.Preferences.Mode = InCallFeedbackMode.Both; }
            if (stage is "call") { fixture.Call.State = CallState.Active; }
            if (stage is "cancel") { cancelled.Cancel(); }
        };
        Func<Task> action = apply
            ? () => fixture.Service.SelectAsync(fixture.Service.Choices[0], false, fixture.Policy.Current.Revision,
                RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, fixture.Policy, () => fixture.Eligible, cancelled.Token)
            : () => fixture.Service.RefreshAsync(RequestOrigin.LocalUi, fixture.Policy.Current.Revision, () => fixture.Eligible, cancelled.Token);
        await action.Should().ThrowAsync<Exception>();
        fixture.Service.Get().Available.Should().BeFalse();
        if (apply) { fixture.Preferences.Pending.Should().BeTrue(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_or_unreadable_state_is_not_default(bool observe)
    {
        await using var fixture = new Fixture();
        fixture.Preferences.ReadFailure = new InvalidDataException();
        Func<Task> action = observe ? () => { fixture.Service.Observe(); return Task.CompletedTask; } : fixture.Refresh;
        await action.Should().ThrowAsync<InvalidDataException>();
        fixture.Service.Get().Desired.Should().BeNull();
        fixture.Service.Get().Source.Should().Be("unavailable");
    }

    [Fact]
    public async Task Cancellation_hostile_provenance_and_stale_policy_have_no_write_effect()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        await FluentActions.Awaiting(() => fixture.Apply(fixture.Service.Choices[0], initiator: SecurityAuditInitiator.ModelSuggestion))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => fixture.Apply(fixture.Service.Choices[0], initiator: SecurityAuditInitiator.VoiceCommand))
            .Should().ThrowAsync<InvalidOperationException>();
        using (var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            await FluentActions.Awaiting(fixture.Refresh).Should().ThrowAsync<InvalidOperationException>();
        }
        fixture.Call.State = CallState.Active;
        (await fixture.Apply(fixture.Service.Choices[0])).Should().BeFalse();
        fixture.Eligible = false;
        await FluentActions.Awaiting(fixture.Refresh).Should().ThrowAsync<InvalidOperationException>();
        fixture.Eligible = true;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await FluentActions.Awaiting(() => fixture.Service.RefreshAsync(RequestOrigin.LocalUi, fixture.Policy.Current.Revision,
            static () => true, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Requested_audit_race_revalidates_host_and_notification_reentrancy_cannot_replace_commit()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = _ => fixture.Eligible = false;
        (await fixture.Select(InCallFeedbackMode.Voice)).Should().BeFalse();
        fixture.Eligible = true;
        fixture.Audit.BeforeWrite = null;
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = _ => fixture.Service.Observe();
        await FluentActions.Awaiting(() => fixture.Select(InCallFeedbackMode.Voice)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Audit.BeforeWrite = null;
        Task? reentrant = null;
        fixture.Service.Changed += (_, _) => reentrant ??= fixture.Refresh();
        await fixture.Refresh();
        await FluentActions.Awaiting(async () => await reentrant!).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Enabled_source_generated_failure_logging_and_notification_failure_retain_explicit_recovery()
    {
        await using var fixture = new Fixture();
        var log = new EnabledLogger();
        var service = new InCallFeedbackConfigurationService(fixture.Preferences, fixture.Admission, fixture.Audit, fixture.Playback, log);
        fixture.Preferences.ReadFailure = new InvalidDataException("fixture");
        FluentActions.Invoking(service.Observe).Should().Throw<InvalidDataException>();
        log.Events.Should().Contain(4911);
        fixture.Preferences.ReadFailure = null;
        await fixture.Refresh();
        fixture.Service.Changed += (_, _) =>
        {
            if (fixture.Preferences.Mode is not null) { fixture.Eligible = false; }
        };
        await FluentActions.Awaiting(() => fixture.Select(InCallFeedbackMode.Voice)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Pending.Should().BeTrue();
        fixture.Service.Get().Available.Should().BeFalse();
    }

    private sealed class EnabledLogger : ILogger<InCallFeedbackConfigurationService>
    {
        internal List<int> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add(eventId.Id);
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
        internal InCallFeedbackConfigurationService Service { get; }
        internal InCallFeedbackConfigurationService CreateService() =>
            new(Preferences, Admission, Audit, Playback, NullLogger<InCallFeedbackConfigurationService>.Instance);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, Policy.Current.Revision, () => Eligible, Token);
        internal Task<bool> Select(InCallFeedbackMode mode, bool reset = false) => Apply(Service.Choices.Single(choice => choice.Mode == mode), reset: reset);
        internal Task<bool> Apply(InCallFeedbackChoice choice, RequestOrigin origin = RequestOrigin.LocalUi,
            long? callRevision = null, SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand, bool reset = false) =>
            Service.SelectAsync(choice, reset, callRevision ?? choice.CallRevision, origin, initiator, Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }
    private sealed class Preferences : IInCallFeedbackPreferences
    {
        internal bool Pending { get; set; }
        internal InCallFeedbackMode? Mode { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? SaveFailure { get; set; }
        internal Exception? BeginFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal int Writes { get; private set; }
        public InCallFeedbackMode? Load() => Pending ? throw new InvalidDataException("unconfirmed") : ReadBack();
        public InCallFeedbackMode? ReadBack() { if (ReadFailure is { } error) { throw error; } return Mode; }
        public void BeginWrite() { if (BeginFailure is { } error) { throw error; } Pending = true; }
        public void ConfirmWrite()
        {
            if (ConfirmFailure is { } error) { throw error; }
            Pending = false;
            AfterConfirm?.Invoke();
        }
        public void Save(InCallFeedbackMode mode) { if (SaveFailure is { } error) { throw error; } Mode = mode; Writes++; AfterWrite?.Invoke(); }
        public void Reset() { if (SaveFailure is { } error) { throw error; } Mode = null; Writes++; AfterWrite?.Invoke(); }
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
        internal CallState State { get => state; set { state = value; StateChanged?.Invoke(this, new(Enum.IsDefined(value) ? value : CallState.Unknown)); } }
        public CallState CurrentState => State;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    }
    private sealed class Playback : ISpeechPlaybackService
    {
        internal int Invalidations { get; private set; }
        internal Exception? Failure { get; set; }
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { if (Failure is { } error) { throw error; } Invalidations++; }
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Configuration must never speak.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
