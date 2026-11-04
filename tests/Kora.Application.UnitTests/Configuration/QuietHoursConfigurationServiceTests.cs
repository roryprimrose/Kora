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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class QuietHoursConfigurationServiceTests
{
    [Fact]
    public async Task BootstrapFailureIsExplicitAndGeneratedDiagnosticsContainNoScheduleContent()
    {
        await using var f = new Fixture();
        f.Preferences.Value = QuietHoursSchedule.Weekly("Mon", "09:00", "17:00", "Australia/Sydney");
        f.Preferences.Pending = true;
        var logger = new RecordingLogger();
        var service = new QuietHoursConfigurationService(f.Preferences, f.Admission, f.Audit, f.Time, logger);
        service.Get().Effective.Should().BeNull();
        service.Get().SavedStateKnown.Should().BeFalse();
        logger.Messages.Should().ContainSingle().Which.Should().Contain(nameof(InvalidDataException))
            .And.NotContain("Australia").And.NotContain("Sydney").And.NotContain("09:00").And.NotContain("Mon");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DiscoveryAndReviewCannotPublishAfterAuthorityOrSavedStateChangesAtTheirReceipt(bool reviewing, bool savedChanged)
    {
        await using var f = new Fixture();
        f.Service.SpeechAdmissionChanged += static () => { };
        f.Store.AfterOperation = () =>
        {
            if (savedChanged) { f.Preferences.Value = QuietHoursSchedule.Off; }
            else { f.Eligible = false; }
        };
        if (reviewing) { await FluentActions.Awaiting(() => f.Review()).Should().ThrowAsync<InvalidOperationException>(); }
        else
        {
            await FluentActions.Awaiting(() => f.Service.InspectAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
            f.Service.Get().Effective.Should().BeNull();
        }
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedChangeCannotBecomeEffectiveAfterTheDurableControlReceiptChangesAuthorityOrSavedState(bool savedChanged)
    {
        await using var f = new Fixture();
        var review = await f.Review();
        f.Service.SpeechAdmissionChanged += static () => { };
        f.Store.AfterOperation = () =>
        {
            if (savedChanged) { f.Preferences.Value = QuietHoursSchedule.Off; }
            else { f.Eligible = false; }
        };
        await FluentActions.Awaiting(() => f.Confirm(review)).Should().ThrowAsync<InvalidOperationException>();
        f.Service.Get().Effective.Should().BeNull();
        f.Preferences.Pending.Should().BeTrue();
    }

    [Fact]
    public async Task BusyControlsRefuseReentrancyAndInspectionNotifiesSpeechAdmission()
    {
        await using var f = new Fixture();
        var notifications = 0;
        f.Service.SpeechAdmissionChanged += () => notifications++;
        await f.Service.InspectAsync(RequestOrigin.LocalUi, () => true, f.Token);
        notifications.Should().Be(1);
        var review = await f.Review();
        Task? reentrant = null;
        f.Audit.BeforeWrite = record =>
        {
            if (record.Outcome == SecurityAuditOutcome.Requested)
            { reentrant = f.Service.InspectAsync(RequestOrigin.LocalUi, () => true, f.Token); }
        };
        (await f.Confirm(review)).Should().BeTrue();
        await FluentActions.Awaiting(() => reentrant!).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.TypedCommand, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.VoiceCommand, RequestOrigin.ActivatedVoice)]
    public async Task OriginalChannelsShareSeparateReviewAtomicConfirmRestartGetAndReset(SecurityAuditInitiator initiator, RequestOrigin origin)
    {
        await using var f = new Fixture();
        f.Service.Get().Effective.Should().Be(QuietHoursSchedule.Off);
        var review = await f.Review(origin: origin, initiator: initiator);
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Records.Should().BeEmpty();
        f.Service.Get().Effective.Should().Be(QuietHoursSchedule.Off);
        (await f.Confirm(review, origin, initiator)).Should().BeTrue();
        f.Service.Get().Saved.Should().Be(f.Schedule);
        f.Service.Get().Effective.Should().Be(f.Schedule);
        f.CreateService().Get().Effective.Should().Be(f.Schedule);
        f.Audit.Records.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Preferences.Pending.Should().BeFalse();
        var reset = await f.Review(value: null, reset: true, origin, initiator);
        f.Preferences.Value.Should().Be(f.Schedule);
        (await f.Confirm(reset, origin, initiator)).Should().BeTrue();
        f.Service.Get().Saved.Should().BeNull();
        f.Service.Get().Effective.Should().Be(QuietHoursSchedule.Off);
        f.Preferences.Writes.Should().Be(2);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("begin")]
    [InlineData("confirm")]
    [InlineData("readback")]
    [InlineData("terminal-audit")]
    [InlineData("requested-audit")]
    [InlineData("intent-terminal")]
    [InlineData("after-write-authority")]
    [InlineData("after-confirm-authority")]
    [InlineData("after-confirm-saved")]
    public async Task FailedOrUnconfirmedWritesNeverPublishEffectiveSuccess(string change)
    {
        await using var f = new Fixture();
        var candidate = await f.Review();
        f.Preferences.Failure = change;
        if (change is "intent-terminal") { f.Store.FailTerminal = true; }
        f.Preferences.AfterWrite = () => { if (change is "after-write-authority") { f.Eligible = false; } };
        f.Preferences.AfterConfirm = () =>
        {
            if (change is "after-confirm-authority") { f.Eligible = false; }
            if (change is "after-confirm-saved") { f.Preferences.Value = QuietHoursSchedule.Off; }
        };
        f.Audit.BeforeWrite = record =>
        {
            if (string.Equals(change, record.Outcome == SecurityAuditOutcome.Requested ? "requested-audit" : "terminal-audit", StringComparison.Ordinal))
            { throw new IOException("injected audit failure"); }
        };
        await FluentActions.Awaiting(() => f.Confirm(candidate)).Should().ThrowAsync<Exception>();
        f.Service.Get().Effective.Should().BeNull();
        f.Service.Get().SavedStateKnown.Should().BeFalse();
        f.Service.Get().Description.Should().Contain("unknown").And.Contain("unconfirmed");
        f.Service.AllowsSpeech(f.Service.SpeechRevision).Should().BeFalse();
        if (change is "confirm" or "readback" or "terminal-audit" or "after-write-authority"
            or "after-confirm-authority" or "after-confirm-saved")
        { f.Preferences.Pending.Should().BeTrue(); f.CreateService().Get().Effective.Should().BeNull(); }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("supersede")]
    [InlineData("host")]
    [InlineData("saved")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("channel")]
    [InlineData("native-vs-typed")]
    [InlineData("expiry")]
    [InlineData("rollback")]
    [InlineData("cancel-token")]
    public async Task StaleReviewCannotCommitOrEmitSuccessfulAudit(string change)
    {
        await using var f = new Fixture();
        var candidate = await f.Review();
        switch (change)
        {
            case "cancel": f.Service.Cancel(); break;
            case "supersede": await f.Review(); break;
            case "host": f.Eligible = false; break;
            case "saved": f.Preferences.Value = QuietHoursSchedule.Off; break;
            case "session": f.Store.Authority = f.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; break;
            case "generation": f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; break;
            case "expiry": f.Time.Now = candidate.Expires; break;
            case "rollback": f.Time.Now = candidate.Created.AddTicks(-1); break;
        }
        using var cancel = new CancellationTokenSource();
        if (change is "cancel-token") { cancel.Cancel(); }
        var origin = change is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        var initiator = change is "channel" ? SecurityAuditInitiator.VoiceCommand
            : change is "native-vs-typed" ? SecurityAuditInitiator.LocalUser : SecurityAuditInitiator.TypedCommand;
        await FluentActions.Awaiting(() => f.Service.ConfirmAsync(candidate, origin, initiator, f.Policy,
            () => f.Eligible, cancel.Token)).Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Records.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task ProtectedCallsRefuseVoiceButNativePolicyStillOwnsItsOwnChoice(CallState call)
    {
        await using var f = new Fixture();
        f.Calls.Set(call);
        await FluentActions.Awaiting(() => f.Review(origin: RequestOrigin.ActivatedVoice, initiator: SecurityAuditInitiator.VoiceCommand))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        var native = await f.Review(initiator: SecurityAuditInitiator.LocalUser);
        (await f.Confirm(native, initiator: SecurityAuditInitiator.LocalUser)).Should().BeTrue();
        f.Policy.Current.IsProtected.Should().BeTrue();
    }

    [Theory]
    [InlineData("call")]
    [InlineData("host")]
    [InlineData("saved")]
    public async Task RequestedAuditRechecksCallAuthorityAndSavedIntent(string change)
    {
        await using var f = new Fixture();
        var candidate = await f.Review();
        f.Audit.BeforeWrite = record =>
        {
            if (record.Outcome != SecurityAuditOutcome.Requested) { return; }
            if (change is "call") { f.Calls.Set(CallState.Active); }
            if (change is "host") { f.Eligible = false; }
            if (change is "saved") { f.Preferences.Value = QuietHoursSchedule.Off; }
        };
        if (change is "saved") { await FluentActions.Awaiting(() => f.Confirm(candidate)).Should().ThrowAsync<InvalidDataException>(); }
        else { (await f.Confirm(candidate)).Should().BeFalse(); }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Records.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.System)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand)]
    public async Task FabricatedOriginalChannelsCannotCreateReviews(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var f = new Fixture();
        await FluentActions.Awaiting(() => f.Review(origin: origin, initiator: initiator)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task HostSystemCannotRelabelAmbientAuthorityAndUnconfirmedInspectionFailsClosed()
    {
        await using var f = new Fixture();
        using (var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await FluentActions.Awaiting(() => f.Review()).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Preferences.Pending = true;
        await FluentActions.Awaiting(() => f.Service.InspectAsync(RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidDataException>();
        f.Service.Get().Effective.Should().BeNull();
        await FluentActions.Awaiting(() => f.Review()).Should().ThrowAsync<InvalidDataException>();
        f.Service.Get().Recovery.Should().NotBeNull();
    }

    [Fact]
    public async Task BoundaryGenerationClockRollbackZoneChangesAndBusyControlFailClosed()
    {
        await using var f = new Fixture();
        f.Time.Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        var old = f.Service.SpeechRevision;
        f.Service.AllowsSpeech(old).Should().BeTrue();
        var review = await f.Review();
        (await f.Confirm(review)).Should().BeTrue();
        f.Service.AllowsSpeech(old).Should().BeFalse();
        var revision = f.Service.SpeechRevision;
        f.Service.StartSpeech(revision, static () => { });
        f.Time.Now = f.Time.Now.AddHours(1);
        f.Service.AllowsSpeech(revision).Should().BeFalse();
        f.Time.Now = f.Time.Now.AddHours(8);
        f.Service.AllowsSpeech(revision).Should().BeTrue();
        f.Time.Now = f.Time.Now.AddMinutes(-1);
        FluentActions.Invoking(() => f.Service.StartSpeech(revision, static () => { })).Should().Throw<OperationCanceledException>();
        f.Time.Now = f.Time.Now.AddMinutes(2);
        var zoneChange = await f.Review(QuietHoursSchedule.Weekly("Mon", "09:00", "17:00", "Australia/Sydney"));
        f.Service.SpeechAdmissionChanged += () => f.Service.AllowsSpeech(f.Service.SpeechRevision).Should().BeFalse();
        (await f.Confirm(zoneChange)).Should().BeTrue();
        f.Service.AllowsSpeech(revision).Should().BeFalse();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal Fixture(TimeProvider? time = null)
        {
            ActivitySource.AddActivityListener(listener);
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Policy = new(Calls);
            Service = CreateService(time);
        }
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal Clock Time { get; } = new();
        internal Calls Calls { get; } = new();
        internal CallCommunicationPolicy Policy { get; }
        internal QuietHoursConfigurationService Service { get; }
        internal QuietHoursSchedule Schedule { get; } = QuietHoursSchedule.Weekly("Mon,Tue,Wed,Thu,Fri", "09:00", "17:00", "UTC");
        internal bool Eligible { get; set; } = true;
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal QuietHoursConfigurationService CreateService(TimeProvider? time = null) =>
            new(Preferences, Admission, Audit, time ?? Time, NullLogger<QuietHoursConfigurationService>.Instance);
        internal Task<QuietHoursReview> Review(QuietHoursSchedule? value = null, bool reset = false,
            RequestOrigin origin = RequestOrigin.LocalUi, SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.ReviewAsync(reset ? null : value ?? Schedule, origin, initiator, Policy.Current.Revision, Policy, () => Eligible, Token);
        internal Task<bool> Confirm(QuietHoursReview candidate, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.ConfirmAsync(candidate, origin, initiator, Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); listener.Dispose(); }
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class Preferences : IQuietHoursPreferences
    {
        internal QuietHoursSchedule? Value { get; set; }
        internal bool Pending { get; set; }
        internal int Writes { get; private set; }
        internal string? Failure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        public QuietHoursSchedule? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed quiet-hours write"); }
            return ReadBack();
        }
        public QuietHoursSchedule? ReadBack() => Failure is "readback" && Writes > 0 ? QuietHoursSchedule.Off : Value;
        public void BeginWrite() { if (Failure is "begin") { throw new IOException("begin failed"); } Pending = true; }
        public void ConfirmWrite() { if (Failure is "confirm") { throw new IOException("confirmation failed"); } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(QuietHoursSchedule? schedule)
        {
            if (Failure is "write") { throw new IOException("write failed"); }
            Value = schedule;
            Writes++;
            AfterWrite?.Invoke();
        }
    }

    internal sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Records { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent entry)
        {
            HostActivity.RequireCurrent().CorrelationId.Should().Be(entry.CorrelationId);
            BeforeWrite?.Invoke(entry);
            Records.Add(entry);
        }
    }

    internal sealed class Calls : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
    }

    private sealed class RecordingLogger : ILogger<QuietHoursConfigurationService>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
