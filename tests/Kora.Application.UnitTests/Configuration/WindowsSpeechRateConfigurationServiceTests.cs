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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class WindowsSpeechRateConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public WindowsSpeechRateConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Confirmed_native_rate_reset_and_restart_have_correlated_receipts_and_no_synthesis()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Available.Should().BeFalse();
        await fixture.Refresh();
        fixture.Service.Get().Source.Should().Be("default");
        fixture.Service.Observe();
        foreach (var rate in new[] { "-10", "0", "10" })
        {
            (await fixture.Set(rate)).Should().BeTrue();
            fixture.Service.Get().Desired.Should().Be(WindowsSpeechRate.Parse(rate));
            fixture.Playback.Rate.Should().Be(WindowsSpeechRate.Parse(rate));
        }
        var restarted = fixture.CreateService();
        restarted.Observe();
        restarted.Get().Source.Should().Be("saved");
        fixture.Preferences.Value = null;
        restarted.Observe();
        restarted.Get().Source.Should().Be("default");
        fixture.Preferences.Value = new(10);
        await fixture.Refresh();
        (await fixture.Set(null)).Should().BeTrue();
        fixture.Preferences.Value.Should().BeNull();
        fixture.Playback.Rate.Should().Be(WindowsSpeechRate.Default);
        fixture.Audit.Events.Chunk(2).Should().OnlyContain(pair =>
            pair[0].Outcome == SecurityAuditOutcome.Requested && pair[1].Outcome == SecurityAuditOutcome.Succeeded);
        fixture.Store.Authority!.IsActive.Should().BeTrue();
        fixture.Audit.Requests.Should().OnlyContain(request => request.SessionId == fixture.Store.Authority.SessionId);
        fixture.Audit.Traces.Should().OnlyContain(context => context.TraceId != default && context.SpanId != default);
    }

    [Theory]
    [InlineData("kokoro")]
    [InlineData("unknown")]
    [InlineData("adapter")]
    [InlineData("unadvertised")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("not-installed")]
    [InlineData("invalid-selection")]
    public async Task Provider_and_adapter_support_fail_closed_without_a_fake_common_scale(string kind)
    {
        await using var fixture = new Fixture(unsupported: string.Equals(kind, "adapter", StringComparison.Ordinal));
        if (kind is "kokoro" or "unknown") { fixture.Selection.Provider = kind is "kokoro" ? SpeechProviderIds.Kokoro : "unknown"; }
        if (kind is "unadvertised") { fixture.Catalog.Providers = fixture.Catalog.Providers.Select(p => p with { RateSupport = SpeechRateSupport.Unsupported }).ToArray(); }
        if (kind is "duplicate") { fixture.Catalog.Providers = [fixture.Catalog.Providers[0], fixture.Catalog.Providers[0]]; }
        if (kind is "missing") { fixture.Catalog.Providers = []; }
        if (kind is "not-installed") { fixture.Catalog.Providers = fixture.Catalog.Providers.Select(p => p with { IsInstalled = false }).ToArray(); }
        if (kind is "invalid-selection") { fixture.Selection.Failure = new InvalidDataException(); }
        fixture.Speech.Reload();
        await fixture.Refresh();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Get().Recovery.Should().Contain("Kokoro synthesis is unchanged");
        fixture.Service.Invoking(service => service.Propose("1", service.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        fixture.Preferences.Writes.Should().Be(0);
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
    [InlineData("provider")]
    [InlineData("provider-revision")]
    public async Task Original_input_session_generation_source_and_provider_are_not_replaceable(string hostile)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var proposal = fixture.Propose("1");
        var origin = RequestOrigin.LocalUi;
        if (hostile is "foreign")
        {
            var foreign = fixture.CreateService();
            await foreign.RefreshAsync(origin, () => fixture.Eligible, fixture.Token);
            proposal = foreign.Propose("1", foreign.Get().Revision, 0);
        }
        if (hostile is "lookalike") { proposal = new(proposal.Value, proposal.Revision, proposal.CallRevision, proposal.Session, proposal.Origin, proposal.Eligible); }
        if (hostile is "revision") { fixture.Service.HoldUnavailable(); await fixture.Refresh(); }
        if (hostile is "preference") { fixture.Preferences.Value = new(9); }
        if (hostile is "session") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (hostile is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (hostile is "inactive") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
        if (hostile is "host") { fixture.Eligible = false; }
        if (hostile is "captured-host") { fixture.CapturedEligible = false; }
        if (hostile is "origin") { origin = RequestOrigin.ActivatedVoice; }
        if (hostile is "provider") { fixture.Selection.Provider = SpeechProviderIds.Kokoro; fixture.Speech.Reload(); }
        if (hostile is "provider-revision") { fixture.Catalog.Voices = []; fixture.Speech.Reload(); }
        await FluentActions.Awaiting(() => fixture.Apply(proposal, origin)).Should().ThrowAsync<InvalidOperationException>();
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
    [InlineData("provider-after-receipt")]
    [InlineData("terminal-change")]
    [InlineData("terminal-null")]
    [InlineData("begin-write")]
    [InlineData("confirm-write")]
    [InlineData("confirmed-read")]
    [InlineData("confirmed-access")]
    [InlineData("confirmed-change")]
    [InlineData("confirmed-null")]
    [InlineData("invalidate")]
    [InlineData("apply-native")]
    public async Task Persistence_and_lost_evidence_never_activate_an_unconfirmed_rate_on_restart(string stage)
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
        if (stage is "host-after-receipt" or "provider-after-receipt")
        {
            fixture.Store.BeforeCommit = record =>
            {
                if (!record.IsTerminal) { return; }
                if (stage is "host-after-receipt") { fixture.Eligible = false; }
                else { fixture.Selection.Provider = SpeechProviderIds.Kokoro; fixture.Speech.Reload(); }
            };
        }
        if (stage is "terminal-change" or "terminal-null")
        {
            fixture.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.Preferences.Value = stage is "terminal-null" ? null : new WindowsSpeechRate(2); }
            };
        }
        if (stage is "invalidate") { fixture.Playback.Failure = new InvalidOperationException(); }
        if (stage is "begin-write") { fixture.Preferences.BeginFailure = new IOException(); }
        if (stage is "confirm-write") { fixture.Preferences.ConfirmFailure = new IOException(); }
        if (stage.StartsWith("confirmed", StringComparison.Ordinal) || stage is "apply-native")
        {
            fixture.Preferences.AfterConfirm = () =>
            {
                if (stage is "confirmed-read") { fixture.Preferences.ReadFailure = new IOException(); }
                else if (stage is "confirmed-access") { fixture.Preferences.ReadFailure = new UnauthorizedAccessException(); }
                else if (stage is "apply-native") { fixture.Playback.SetFailure = new InvalidOperationException(); }
                else { fixture.Preferences.Value = stage is "confirmed-null" ? null : new WindowsSpeechRate(2); }
            };
        }
        await FluentActions.Awaiting(() => fixture.Apply(proposal)).Should().ThrowAsync<Exception>();
        fixture.Service.Get().Available.Should().BeFalse();
        fixture.Service.Get().Recovery.Should().Contain("unconfirmed");
        if (stage is "invalidate") { fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed); }
        if (fixture.Preferences.Pending)
        {
            fixture.Preferences.ReadFailure = null;
            fixture.Playback.Failure = null;
            fixture.Playback.SetFailure = null;
            fixture.CreateService().Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public async Task Protected_unknown_and_stale_calls_deny_original_voice_but_allow_fresh_local_admission()
    {
        await using var fixture = new Fixture();
        fixture.Call.State = CallState.Unknown;
        fixture.Policy.LoadSettings(new(true, false));
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => fixture.Eligible, fixture.Token);
        (await fixture.Apply(fixture.Propose("-10"), RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        fixture.Service.Get().Effective.Should().Be(WindowsSpeechRate.Default);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        await fixture.Refresh();
        var stale = fixture.Service.Propose("1", fixture.Service.Get().Revision, 99);
        (await fixture.Apply(stale)).Should().BeFalse();
        await FluentActions.Awaiting(() => fixture.Apply(stale, RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => fixture.Apply(stale, RequestOrigin.LocalUi, (SecurityAuditInitiator)999)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Call.State = CallState.Active;
        await fixture.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => fixture.Eligible, fixture.Token);
        (await fixture.Apply(fixture.Propose("1"), RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        await fixture.Refresh();
        (await fixture.Set("1")).Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_and_reentrant_proposals_require_live_original_admission()
    {
        await using var fixture = new Fixture();
        fixture.Service.Invoking(s => s.Propose("0", 0, 0)).Should().Throw<InvalidOperationException>();
        fixture.Preferences.ReadFailure = new InvalidDataException();
        await FluentActions.Awaiting(fixture.Refresh).Should().ThrowAsync<InvalidDataException>();
        fixture.Preferences.ReadFailure = null;
        await fixture.Refresh();
        fixture.Service.Invoking(s => s.Propose("01", s.Get().Revision, 0)).Should().Throw<ArgumentOutOfRangeException>();
        fixture.Service.Invoking(s => s.Propose("0", 99, 0)).Should().Throw<InvalidOperationException>();
        fixture.CapturedEligible = false;
        fixture.Service.Invoking(s => s.Propose("0", s.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        await fixture.Service.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
        fixture.CapturedEligible = true;
        fixture.Store.BeforeCommit = record => { if (record.IsTerminal) { fixture.Preferences.Value = new(1); } };
        await FluentActions.Awaiting(fixture.Refresh).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.BeforeCommit = record => { if (record.IsTerminal) { fixture.CapturedEligible = false; } };
        await FluentActions.Awaiting(fixture.Refresh).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.BeforeCommit = null;
        fixture.CapturedEligible = true;
        await fixture.Refresh();
        fixture.Audit.BeforeWrite = _ => fixture.Service.Observe();
        await FluentActions.Awaiting(() => fixture.Set("0")).Should().ThrowAsync<InvalidOperationException>();
        fixture.Audit.BeforeWrite = null;
        fixture.Service.Changed += (_, _) =>
        {
            fixture.Service.Invoking(s => s.Observe()).Should().Throw<InvalidOperationException>();
            fixture.Service.Invoking(s => s.Propose("0", s.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        };
        await fixture.Refresh();
        (await fixture.Set(null)).Should().BeTrue();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("captured-host")]
    [InlineData("provider")]
    [InlineData("source")]
    [InlineData("cancel")]
    public async Task Requested_audit_revalidates_live_authority_before_save(string changed)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        using var cancellation = new CancellationTokenSource();
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested) { return; }
            if (changed is "host") { fixture.Eligible = false; }
            if (changed is "captured-host") { fixture.CapturedEligible = false; }
            if (changed is "provider") { fixture.Catalog.Voices = []; fixture.Speech.Reload(); }
            if (changed is "source") { fixture.Preferences.Value = new(9); }
            if (changed is "cancel") { cancellation.Cancel(); }
        };
        var apply = () => fixture.Service.ApplyAsync(fixture.Propose("1"), RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            fixture.Policy, () => fixture.Eligible, cancellation.Token);
        if (changed is "source") { await apply.Should().ThrowAsync<InvalidDataException>(); }
        else { (await apply()).Should().BeFalse(); }
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_and_disposed_admission_never_save()
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var candidate = fixture.Propose("1");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(candidate, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            fixture.Policy, () => fixture.Eligible, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await fixture.Admission.DisposeAsync();
        await FluentActions.Awaiting(fixture.Refresh).Should().ThrowAsync<ObjectDisposedException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("source")]
    [InlineData("selection")]
    [InlineData("effective-voice")]
    [InlineData("providers")]
    [InlineData("voices")]
    public async Task Live_selection_and_catalogue_are_revalidated_even_without_a_change_notification(string change)
    {
        await using var fixture = new Fixture();
        await fixture.Refresh();
        var revision = fixture.Speech.Get().Revision;
        fixture.Speech.IsCurrentSelection(revision).Should().BeTrue();
        if (change is "revision") { revision++; }
        if (change is "source") { fixture.Selection.Saved = false; }
        if (change is "selection") { fixture.Selection.Provider = SpeechProviderIds.Kokoro; }
        if (change is "effective-voice") { fixture.Catalog.Voices = []; }
        if (change is "providers") { fixture.Catalog.Providers = fixture.Catalog.Providers.Select(p => p with { Description = "changed" }).ToArray(); }
        if (change is "voices") { fixture.Catalog.Voices = [.. fixture.Catalog.Voices, new("other", "Other", "en-US", SpeechVoiceGender.Male)]; }
        fixture.Speech.IsCurrentSelection(revision).Should().BeFalse();
        if (change is not "revision")
        {
            fixture.Service.Invoking(s => s.Propose("1", s.Get().Revision, 0)).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task Invalid_saved_rate_is_logged_explicitly_and_two_audio_sessions_do_not_share_audit_authority()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        await first.Refresh();
        await second.Refresh();
        (await first.Set("1")).Should().BeTrue();
        (await second.Set("-1")).Should().BeTrue();
        first.Audit.Requests.Select(r => r.SessionId).Should().NotIntersectWith(second.Audit.Requests.Select(r => r.SessionId));
        first.Preferences.ReadFailure = new InvalidDataException();
        await FluentActions.Awaiting(first.Refresh).Should().ThrowAsync<InvalidDataException>();
        first.Logger.Events.Should().Contain(4901);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(bool unsupported = false)
        {
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Policy = new(Call);
            Speech = new(Selection, Catalog, Audit, NullLogger<SpeechConfigurationService>.Instance);
            Service = new(Preferences, unsupported ? new UnsupportedPlayback() : Playback, Speech, Admission, Audit,
                Logger);
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal bool CapturedEligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal SelectionPreferences Selection { get; } = new();
        internal Catalog Catalog { get; } = new();
        internal Playback Playback { get; } = new();
        internal Audit Audit { get; } = new();
        internal RecordingLogger Logger { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy Policy { get; }
        internal SpeechConfigurationService Speech { get; }
        internal WindowsSpeechRateConfigurationService Service { get; }
        internal WindowsSpeechRateConfigurationService CreateService() => new(Preferences, Playback, Speech, Admission, Audit,
            Logger);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => CapturedEligible, Token);
        internal WindowsSpeechRateProposal Propose(string? value) => Service.Propose(value, Service.Get().Revision, Policy.Current.Revision);
        internal Task<bool> Set(string? value) => Apply(Propose(value));
        internal Task<bool> Apply(WindowsSpeechRateProposal proposal, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.ApplyAsync(proposal, origin, initiator, Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    private sealed class Preferences : IWindowsSpeechRatePreferences
    {
        internal WindowsSpeechRate? Value { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal int Writes { get; private set; }
        internal Action? AfterWrite { get; set; }
        internal bool Pending { get; private set; }
        internal Exception? BeginFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterConfirm { get; set; }
        public WindowsSpeechRate? Load() { if (Pending) { throw new InvalidDataException("Unconfirmed write"); } return ReadBack(); }
        public WindowsSpeechRate? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() { if (BeginFailure is { } failure) { throw failure; } Pending = true; }
        public void ConfirmWrite() { if (ConfirmFailure is { } failure) { throw failure; } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(WindowsSpeechRate rate) { if (WriteFailure is { } failure) { throw failure; } Value = rate; Writes++; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; Writes++; }
    }
    private sealed class SelectionPreferences : ITextToSpeechPreferences
    {
        internal string Provider { get; set; } = SpeechProviderIds.Windows;
        internal Exception? Failure { get; set; }
        internal bool Saved { get; set; } = true;
        public SpeechSelection? LoadSelection() => Failure is { } failure ? throw failure : Saved ? new(Provider, null) : null;
        public SpokenSummaryLimits? LoadSummaryLimits() => null;
        public void SaveSummaryLimits(SpokenSummaryLimits limits) => throw new NotSupportedException();
        public void SaveSelection(SpeechSelection selection) => throw new NotSupportedException();
        public string? LoadProviderId() => Provider;
        public void SaveProviderId(string providerId) => throw new NotSupportedException();
        public string? LoadVoiceId() => null;
        public void SaveVoiceId(string voiceId) => throw new NotSupportedException();
    }
    private sealed class Catalog : ISpeechCatalog
    {
        internal IReadOnlyList<SpeechProvider> Providers { get; set; } =
        [
            new(SpeechProviderIds.Windows, "Windows", "Native", true, true, null, "default") { RateSupport = SpeechRateSupport.WindowsNative },
            new(SpeechProviderIds.Kokoro, "Kokoro", "Unchanged", true, false, null, "default"),
        ];
        internal IReadOnlyList<SpeechVoice> Voices { get; set; } =
        [
            new("default", "Windows", "en-US", SpeechVoiceGender.Female),
            new("default", "Kokoro", "en-US", SpeechVoiceGender.Female) { ProviderId = SpeechProviderIds.Kokoro },
        ];
        public IReadOnlyList<SpeechProvider> GetProviders() => Providers;
        public IReadOnlyList<SpeechVoice> GetVoices() => Voices;
        public SpeechVoice? GetDefaultVoice() => Voices.Count == 0 ? null : Voices[0];
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest> Requests { get; } = [];
        internal List<ActivityContext> Traces { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            BeforeWrite?.Invoke(auditEvent);
            Requests.Add(HostActivity.RequireCurrent().Request);
            Traces.Add(Activity.Current!.Context);
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
    private sealed class Playback : UnsupportedPlayback, IWindowsSpeechRateControl
    {
        internal WindowsSpeechRate? Rate { get; private set; } = WindowsSpeechRate.Default;
        internal Exception? Failure { get; set; }
        internal Exception? SetFailure { get; set; }
        public override void InvalidateOutput() { if (Failure is { } failure) { throw failure; } }
        public void SetWindowsSpeechRate(WindowsSpeechRate? rate) { if (SetFailure is { } failure) { throw failure; } Rate = rate; }
    }
    private sealed class RecordingLogger : ILogger<WindowsSpeechRateConfigurationService>
    {
        internal List<int> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            exception.Should().NotBeNull();
            Events.Add(eventId.Id);
        }
    }
}
