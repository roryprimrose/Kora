using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Communication;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class SpeechTextConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public SpeechTextConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Default_off_exact_discovery_audited_save_readback_restart_and_reset()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Observe();
        fixture.Service.Observe();
        fixture.Service.Get().Effective.Should().Be(SpeechTextMode.Off);
        fixture.Service.Get().Source.Should().Be("default");
        await fixture.Refresh();
        fixture.Service.Choices.Select(choice => choice.Label).Should().Equal("Off", "CurrentUtterance");
        await fixture.Apply();
        fixture.Service.Get().Saved.Should().Be(SpeechTextMode.CurrentUtterance);
        fixture.Service.Get().Source.Should().Be("saved");
        var restart = fixture.Create();
        restart.Observe();
        restart.Get().Effective.Should().Be(SpeechTextMode.CurrentUtterance);
        await fixture.Refresh();
        await fixture.Apply(fixture.Service.Choices[0]);
        fixture.Service.Get().Effective.Should().Be(SpeechTextMode.Off);
        fixture.Audit.Events.Should().HaveCount(4);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        fixture.Audit.Hosts.Should().OnlyContain(host => host != null && host.SessionId == fixture.Store.Authority!.SessionId);
        SpeechTextState.Serialize(fixture.Service.Get()).Should().Contain("\"scope\":\"device-local\"")
            .And.Contain("\"type\":\"speech-text-mode\"").And.Contain("\"schema\":1");
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("origin")]
    [InlineData("host")]
    [InlineData("saved")]
    [InlineData("hold")]
    [InlineData("initiator")]
    [InlineData("voice-origin")]
    public async Task Foreign_stale_unknown_origin_generation_and_ownership_fail_closed(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var choice = fixture.Service.Choices[1];
        if (stage is "foreign")
        {
            var other = fixture.Create();
            await other.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
            choice = other.Choices[1];
        }
        if (stage is "lookalike") { choice = new(choice.Mode, choice.Revision, choice.Owner, choice.Session, choice.Origin, choice.Eligible); }
        if (stage is "revision") { await fixture.Refresh(); }
        if (stage is "session") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (stage is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (stage is "host") { fixture.Eligible = false; }
        if (stage is "saved") { fixture.Preferences.Mode = SpeechTextMode.Off; }
        if (stage is "hold") { fixture.Service.HoldUnavailable(); }
        await fixture.Awaiting(_ => fixture.Service.SelectAsync(choice, fixture.Policy.Current.Revision,
            stage is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            stage is "initiator" ? SecurityAuditInitiator.ModelSuggestion : stage is "voice-origin" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
            fixture.Policy, () => fixture.Eligible, fixture.Token)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("invalid")]
    [InlineData("readback")]
    [InlineData("requested")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("confirm")]
    [InlineData("late-readback")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-load")]
    [InlineData("late-host")]
    public async Task Atomic_evidence_failure_holds_captions_off_including_restart(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        fixture.Preferences.SaveFailure = stage switch
        {
            "io" => new IOException(),
            "access" => new UnauthorizedAccessException(),
            "invalid" => new ArgumentOutOfRangeException(nameof(stage)),
            _ => null,
        };
        fixture.Preferences.AfterSave = () => { if (stage is "readback") { fixture.Preferences.Mode = SpeechTextMode.Off; } };
        fixture.Audit.BeforeWrite = item =>
        {
            if (stage is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || stage is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        fixture.Store.FailTerminal = stage is "receipt";
        fixture.Preferences.ConfirmFailure = stage is "confirm" ? new IOException() : null;
        fixture.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (stage is "late-readback") { fixture.Preferences.Mode = SpeechTextMode.Off; }
            if (stage is "late-host") { fixture.Eligible = false; }
        };
        fixture.Preferences.AfterConfirm = () =>
        {
            if (stage is "confirmed-readback") { fixture.Preferences.Mode = SpeechTextMode.Off; }
            if (stage is "confirmed-load") { fixture.Preferences.LoadFailure = new IOException(); }
        };
        await fixture.Awaiting(_ => fixture.Apply()).Should().ThrowAsync<Exception>();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Get().Effective.Should().BeNull();
        fixture.Service.Choices.Should().BeEmpty();
        if (stage is not "requested")
        {
            fixture.Preferences.Pending.Should().BeTrue();
            fixture.Create().Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public async Task Original_activated_voice_call_policy_refusal_and_cancellation_do_not_write()
    {
        await using var fixture = new Fixture();
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => fixture.Eligible, fixture.Token);
        fixture.Call.State = CallState.Unknown;
        var accepted = await fixture.Service.SelectAsync(fixture.Service.Choices[1], fixture.Policy.Current.Revision,
            RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand, fixture.Policy, () => fixture.Eligible, fixture.Token);
        accepted.Should().BeFalse();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        fixture.Audit.Events.Last().Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        fixture.Preferences.Writes.Should().Be(0);
        await fixture.Awaiting(_ => fixture.Service.RefreshAsync(RequestOrigin.LocalUi, static () => true,
            new CancellationToken(true))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Corruption_and_pending_state_hold_on_observe_or_refresh_without_defaults()
    {
        await using var fixture = new Fixture();
        fixture.Preferences.Mode = (SpeechTextMode)99;
        fixture.Service.Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        fixture.Service.Get().Available.Should().BeFalse();
        await fixture.Awaiting(_ => fixture.Refresh()).Should().ThrowAsync<InvalidDataException>();
        fixture.Preferences.Mode = null;
        fixture.Preferences.Pending = true;
        fixture.Service.Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Reentrant_discovery_observe_and_changed_revision_cannot_cross_a_commit()
    {
        await using var fixture = new Fixture();
        var callbacks = 0;
        fixture.Service.Changed += (_, _) => callbacks++;
        fixture.Service.Observe();
        fixture.Preferences.Mode = SpeechTextMode.CurrentUtterance;
        fixture.Service.Observe();
        await fixture.Refresh();
        fixture.Preferences.AfterSave = () =>
        {
            fixture.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>();
            fixture.Refresh().IsFaulted.Should().BeTrue();
        };
        await fixture.Apply();
        callbacks.Should().BeGreaterThan(0);
        fixture.Store.BeforeCommit = task =>
        {
            if (task.State == HostTaskState.Succeeded) { fixture.Service.HoldUnavailable(); }
        };
        await fixture.Awaiting(_ => fixture.Refresh()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Caption_options_discover_save_restart_and_reset_independently_without_enabling_speech_text()
    {
        await using var fixture = new Fixture();
        fixture.Service.Observe();
        fixture.Service.Get().EffectiveCaptionOptions.Should().Be(SpeechCaptionOptions.Default);
        await fixture.Refresh();
        fixture.Service.CaptionChoices.Should().HaveCount(35);
        fixture.Service.CaptionChoices.Select(choice => choice.Label).Should().Contain("TopLeft").And.Contain("30");
        await fixture.Apply(fixture.Service.CaptionChoices.Single(choice => choice.CaptionValue == new SpeechCaptionValue.Placement(SpeechCaptionPlacement.TopLeft)));
        fixture.Service.Get().Effective.Should().Be(SpeechTextMode.Off);
        fixture.Create().Get().EffectiveCaptionOptions.Should().BeNull();
        var restart = fixture.Create();
        restart.Observe();
        restart.Get().EffectiveCaptionOptions!.Placement.Should().Be(SpeechCaptionPlacement.TopLeft);
        await fixture.Refresh();
        await fixture.Apply(fixture.Service.CaptionChoices.Single(choice => choice.CaptionValue == new SpeechCaptionValue.Delay(30)));
        fixture.Service.Get().EffectiveCaptionOptions.Should().Be(new SpeechCaptionOptions(SpeechCaptionPlacement.TopLeft, 30));
        await fixture.Refresh();
        await fixture.Apply(fixture.Service.CaptionChoices.Single(choice => choice.CaptionValue == new SpeechCaptionValue.Placement(SpeechCaptionPlacement.BottomRight)));
        fixture.Service.Get().EffectiveCaptionOptions.Should().Be(new SpeechCaptionOptions(SpeechCaptionPlacement.BottomRight, 30));
        await fixture.Refresh();
        await fixture.Apply(fixture.Service.CaptionChoices.Single(choice => choice.CaptionValue == new SpeechCaptionValue.Delay(5)));
        fixture.Service.Get().EffectiveCaptionOptions.Should().Be(SpeechCaptionOptions.Default);
        fixture.Audit.Events.Select(item => item.ActionId).Should().Contain("configuration.speech-caption-placement")
            .And.Contain("configuration.speech-caption-dismissal-delay");
        SpeechTextState.Serialize(fixture.Service.Get()).Should().Contain(SpeechTextCommand.DelayId).And.Contain(SpeechTextCommand.PlacementId);
        SpeechTextState.Serialize(fixture.Service.Get(), SpeechCaptionOption.Placement).Should().Contain("\"default\":\"BottomRight\"");
        SpeechTextState.Serialize(fixture.Service.Get(), SpeechCaptionOption.DismissalDelay).Should().Contain("\"maximum\":30");
    }

    [Theory]
    [InlineData("readback")]
    [InlineData("confirm")]
    [InlineData("foreign")]
    [InlineData("changed")]
    [InlineData("io")]
    public async Task Caption_option_failure_and_companion_changes_never_publish_confirmed_defaults(string stage)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var choice = fixture.Service.CaptionChoices[^1];
        if (stage is "foreign")
        {
            var other = fixture.Create();
            await other.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
            choice = other.CaptionChoices[^1];
        }
        if (stage is "changed") { fixture.Preferences.Options = new(SpeechCaptionPlacement.TopLeft, 0); }
        if (stage is "io") { fixture.Preferences.SaveFailure = new IOException(); }
        if (stage is "readback") { fixture.Preferences.AfterSave = () => fixture.Preferences.Options = SpeechCaptionOptions.Default; }
        if (stage is "confirm") { fixture.Preferences.AfterConfirm = () => fixture.Preferences.Options = SpeechCaptionOptions.Default; }
        await fixture.Awaiting(_ => fixture.Apply(choice)).Should().ThrowAsync<Exception>();
        if (stage is "foreign")
        {
            fixture.Service.Get().EffectiveCaptionOptions.Should().Be(SpeechCaptionOptions.Default);
            fixture.Preferences.Writes.Should().Be(0);
        }
        else
        {
            fixture.Service.Get().EffectiveCaptionOptions.Should().BeNull();
            fixture.Service.CaptionChoices.Should().BeEmpty();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture()
        {
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Policy = new(Call);
            Service = Create();
        }
        public bool Eligible { get; set; } = true;
        public Preferences Preferences { get; } = new();
        public Audit Audit { get; } = new();
        public AudioControlTestStore Store { get; } = new();
        public Call Call { get; } = new();
        public AudioControlAdmission Admission { get; }
        public CallCommunicationPolicy Policy { get; }
        public SpeechTextConfigurationService Service { get; }
        public CancellationToken Token => TestContext.Current.CancellationToken;
        public SpeechTextConfigurationService Create() => new(Preferences, Admission, Audit);
        public Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => Eligible, Token);
        public Task<bool> Apply(SpeechTextChoice? choice = null) => Service.SelectAsync(choice ?? Service.Choices[1],
            Policy.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }
    private sealed class Preferences : ISpeechTextPreferences
    {
        public SpeechCaptionOptions? Options { get; set; }
        public SpeechCaptionOptions? LoadOptions() => Pending ? throw new InvalidDataException() : ReadBackOptions();
        public SpeechCaptionOptions? ReadBackOptions() => Options;
        public void SaveOptions(SpeechCaptionOptions options) { if (SaveFailure is { } failure) { throw failure; } Options = options; Writes++; AfterSave?.Invoke(); }
        public SpeechTextMode? Mode { get; set; }
        public bool Pending { get; set; }
        public int Writes { get; private set; }
        public Exception? LoadFailure { get; set; }
        public Exception? SaveFailure { get; set; }
        public Exception? ConfirmFailure { get; set; }
        public Action? AfterSave { get; set; }
        public Action? AfterConfirm { get; set; }
        public SpeechTextMode? Load() => Pending ? throw new InvalidDataException() : ReadBack();
        public SpeechTextMode? ReadBack() { if (LoadFailure is { } failure) { throw failure; } return Mode; }
        public void BeginWrite() => Pending = true;
        public void Save(SpeechTextMode mode) { if (SaveFailure is { } failure) { throw failure; } Mode = mode; Writes++; AfterSave?.Invoke(); }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public List<HostRequest?> Hosts { get; } = [];
        public Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent item) { BeforeWrite?.Invoke(item); Events.Add(item); Hosts.Add(HostActivity.Current?.Request); }
    }
    private sealed class Call : ICallStateService
    {
        private CallState state = CallState.Clear;
        public CallState State { get => state; set { state = value; StateChanged?.Invoke(this, new(value)); } }
        public CallState CurrentState => state;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    }
}
