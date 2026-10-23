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
public sealed class SpeechInputConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public SpeechInputConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task SelectionUsesOneHostPathWithoutOpeningCaptureOrSubstitutingProviders(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var fixture = new Fixture();
        var unavailable = SpeechInputSelection.Default with { Resource = SpeechInputResource.Gpu };
        (await fixture.Select(unavailable, origin, initiator)).Should().BeTrue();
        fixture.Service.Get().Saved.Should().Be(unavailable);
        fixture.Service.Get().Desired.Should().Be(unavailable);
        fixture.Service.Get().Effective.Should().BeNull();
        fixture.Service.Get().UnavailableReason.Should().Contain("not qualified");
        fixture.Runtime.ProfileGeneration.Should().Be(new SpeechInputGeneration(1, unavailable));
        fixture.Runtime.IsProfileAvailable.Should().BeFalse();
        fixture.Runtime.Retirements.Should().Be(1);
        fixture.Runtime.ActiveLease.Should().BeFalse();
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        fixture.Audit.Requests.Should().OnlyContain(request => request.Origin == origin);
        fixture.Audit.Requests.Distinct().Should().ContainSingle();
        fixture.Audit.Contexts.Should().OnlyContain(context => context.TraceId != default && context.SpanId != default);
        (await fixture.Select(SpeechInputSelection.Default, origin, initiator)).Should().BeTrue();
        fixture.Service.Get().Effective!.Number.Should().Be(2);
        fixture.Runtime.IsProfileAvailable.Should().BeTrue();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("confirm")]
    [InlineData("audit")]
    [InlineData("terminal")]
    public async Task UnconfirmedWritesDoNotAdvanceGenerationAndHoldCaptureUnavailable(string failure)
    {
        await using var fixture = new Fixture();
        if (failure is "save") { fixture.Preferences.SaveFailure = true; }
        if (failure is "readback") { fixture.Preferences.MismatchReadback = true; }
        if (failure is "confirm") { fixture.Preferences.ConfirmFailure = true; }
        if (failure is "audit") { fixture.Audit.FailSuccess = true; }
        if (failure is "terminal")
        {
            fixture.Store.BeforeCommit = record =>
            {
                if (record.IsTerminal && fixture.Preferences.Pending) { throw new IOException("fixture terminal receipt failure"); }
            };
        }
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<Exception>();
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Runtime.IsProfileAvailable.Should().BeFalse();
        fixture.Runtime.ActiveLease.Should().BeFalse();
        fixture.Service.Get().Effective.Should().BeNull();
        fixture.Preferences.Pending.Should().BeTrue();
    }

    [Theory]
    [InlineData("eligibility")]
    [InlineData("call-revision")]
    [InlineData("saved")]
    [InlineData("privacy-during-retirement")]
    public async Task StaleOrRevokedInputsNeverWritePreferences(string change)
    {
        await using var fixture = new Fixture();
        if (change is "eligibility") { fixture.Eligible = false; }
        if (change is "saved") { fixture.Preferences.Value = SpeechInputSelection.Default; }
        if (change is "privacy-during-retirement") { fixture.Runtime.BeforeReturn = () => fixture.Eligible = false; }
        if (change is "call-revision")
        {
            (await fixture.Select(SpeechInputSelection.Default, callRevision: -1)).Should().BeFalse();
            fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        }
        else
        {
            await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<Exception>();
        }
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
    }

    [Fact]
    public async Task InvalidStartupPreferenceDoesNotActivateTheBaseline()
    {
        await using var fixture = new Fixture(invalidStartup: true);
        fixture.Service.Get().Desired.Should().BeNull();
        fixture.Service.Get().Effective.Should().BeNull();
        fixture.Runtime.IsProfileAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CancellationJoinsRetirementAndDoesNotPublishAConfiguration()
    {
        await using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.Token = cancellation.Token;
        fixture.Runtime.BeforeReturn = cancellation.Cancel;
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default))
            .Should().ThrowAsync<OperationCanceledException>();
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Runtime.ActiveLease.Should().BeFalse();
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Cancelled);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task ProtectedCallsCannotRelabelOriginalVoiceAsNativeInput(CallState state)
    {
        await using var fixture = new Fixture();
        fixture.Call.Set(state);
        using (var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request))
        {
            (await fixture.Select(SpeechInputSelection.Default, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)).Should().BeFalse();
            fixture.Preferences.Writes.Should().Be(0);
            fixture.Audit.Events.Last().Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
            fixture.Audit.Requests.Should().OnlyContain(request => request.Origin == RequestOrigin.ActivatedVoice);
            original.Complete(HostOperationOutcome.Completed);
        }
        (await fixture.Select(SpeechInputSelection.Default)).Should().BeTrue();
    }

    [Fact]
    public async Task HostSystemContextDoesNotGrantNativeInputAuthority()
    {
        await using var fixture = new Fixture();
        using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Runtime.Retirements.Should().Be(0);
        fixture.Preferences.Writes.Should().Be(0);
        original.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task AdmissionLostDuringConfirmationLeavesPendingEvidenceAndNoNewGeneration()
    {
        await using var fixture = new Fixture();
        fixture.Preferences.AfterConfirm = () => fixture.Eligible = false;
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<InvalidDataException>();
        fixture.Preferences.Pending.Should().BeTrue();
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Runtime.IsProfileAvailable.Should().BeFalse();
        fixture.Service.Get().SavedStateKnown.Should().BeFalse();
    }

    [Theory]
    [InlineData("get speech.input", AppearanceCommandOperation.Get)]
    [InlineData("Nova, status speech.input", AppearanceCommandOperation.Get)]
    [InlineData("set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime", AppearanceCommandOperation.Set)]
    [InlineData("Nova, set speech.input to windows-activated-v1 Gpu KeepLoaded", AppearanceCommandOperation.Set)]
    [InlineData("set speech.input to unknown Cpu KeepLoaded", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.input to windows-activated-v1 0 0", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.input to windows-activated-v1 WindowsManaged,Cpu CaptureLifetime", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.input to windows-activated-v1 Cpu KeepLoaded extra", AppearanceCommandOperation.Clarify)]
    public void ParserAdmitsOnlyCompleteKnownTuples(string text, AppearanceCommandOperation expected)
    {
        SpeechInputCommand.Parse(text, "Nova")!.Operation.Should().Be(expected);
        SpeechInputCommand.Parse("unrelated", "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("get speech.input-device")]
    [InlineData("status speech.input-device")]
    [InlineData("set speech.input-device to mic")]
    [InlineData("Nova, set speech.input-device to mic")]
    [InlineData("get speech.input.other")]
    public void ProfileParserDoesNotInterceptAnotherSetting(string text)
    {
        SpeechInputCommand.Parse(text, "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("get speech.input extra")]
    [InlineData("get speech.input\n")]
    [InlineData("set speech.input")]
    [InlineData("set speech.input to windows-activated-v1 Other CaptureLifetime")]
    [InlineData("set speech.input to windows-activated-v1 WindowsManaged Other")]
    [InlineData("set speech.input to windows-activated-v1 WindowsManaged 99")]
    [InlineData("set speech.input to windows-activated-v1 99 CaptureLifetime")]
    [InlineData("set speech.input to windows-activated-v1 WindowsManaged,Cpu CaptureLifetime")]
    public void ProfileParserRejectsInvalidCompleteInput(string text)
    {
        SpeechInputCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Fact]
    public void ProfileParserRejectsOversizeInputAndNameLookalikes()
    {
        SpeechInputCommand.Parse("Nova get speech.input", "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Get);
        SpeechInputCommand.Parse("get speech.input " + new string('x', 1024), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        SpeechInputCommand.Parse("Koral get speech.input", "Kora").Should().BeNull();
        SpeechInputCommand.Parse("Kora", "Kora").Should().BeNull();
    }

    [Theory]
    [InlineData("saved")]
    [InlineData("unavailable")]
    [InlineData("malformed")]
    [InlineData("access")]
    public async Task StartupKeepsSavedSelectionAndFailureStateSeparate(string kind)
    {
        await using var fixture = new Fixture(startup: kind);
        var state = fixture.Service.Get();
        if (kind is "malformed" or "access")
        {
            state.SavedStateKnown.Should().BeFalse();
            state.Effective.Should().BeNull();
            state.Describe().Should().Contain("unknown / unconfirmed").And.Contain("Desired: unknown");
        }
        else
        {
            state.Saved.Should().NotBeNull();
            state.Describe().Should().Contain("Saved: SpeechInputSelection");
            if (kind is "unavailable") { state.Effective.Should().BeNull(); }
            else { state.Effective.Should().NotBeNull(); }
        }
    }

    [Fact]
    public async Task DefaultInspectionDoesNotImplyASavedPreference()
    {
        await using var fixture = new Fixture();
        fixture.Service.Get().Describe().Should().Contain("not saved (baseline default)")
            .And.Contain("Effective: SpeechInputSelection").And.Contain("Windows activated adapter selected");
        fixture.Runtime.HoldUnavailable();
        fixture.Service.Get().UnavailableReason.Should().Contain("held unavailable or switching");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.VoiceCommand, RequestOrigin.LocalUi)]
    public async Task UntrustedInitiatorsCannotRetireCapture(SecurityAuditInitiator initiator, RequestOrigin origin)
    {
        await using var fixture = new Fixture();
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default, origin, initiator))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Runtime.Retirements.Should().Be(0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("cancel")]
    public async Task RetirementFailureHasOneTerminalAuditWithoutAWrite(string kind)
    {
        await using var fixture = new Fixture();
        fixture.Runtime.RetirementFailure = kind is "io" ? new IOException("retirement failed") : new OperationCanceledException();
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<Exception>();
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            kind is "io" ? SecurityAuditOutcome.Failed : SecurityAuditOutcome.Cancelled);
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Preferences.Writes.Should().Be(0);
        fixture.Service.Get().Effective.Should().BeNull();
    }

    [Theory]
    [InlineData("saved")]
    [InlineData("access")]
    [InlineData("requested-audit")]
    [InlineData("cancel-audit")]
    public async Task AdmissionOrStorageChangesCannotPublishAProfile(string change)
    {
        await using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.Token = cancellation.Token;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            switch (change)
            {
                case "saved": fixture.Preferences.Value = SpeechInputSelection.Default; break;
                case "access": fixture.Preferences.WriteFailure = new UnauthorizedAccessException(); break;
                case "requested-audit": throw new IOException("requested audit unavailable");
                case "cancel-audit": cancellation.Cancel(); break;
            }
        };
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<Exception>();
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("eligible")]
    [InlineData("cancel")]
    [InlineData("call")]
    [InlineData("readback")]
    [InlineData("dispose")]
    public async Task LostTerminalAdmissionCannotActivatePersistedPreference(string change)
    {
        await using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.Token = cancellation.Token;
        Task? disposal = null;
        fixture.Store.BeforeCommit = record =>
        {
            if (!record.IsTerminal || !fixture.Preferences.Pending) { return; }
            switch (change)
            {
                case "eligible": fixture.Eligible = false; break;
                case "cancel": cancellation.Cancel(); break;
                case "call": fixture.Call.Set(CallState.Active); break;
                case "readback": fixture.Preferences.MismatchReadback = true; break;
                case "dispose": disposal = fixture.Service.DisposeAsync().AsTask(); break;
            }
        };
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<InvalidOperationException>();
        if (disposal is not null) { await disposal; }
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Runtime.IsProfileAvailable.Should().BeFalse();
        fixture.Preferences.Pending.Should().BeTrue();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("call")]
    [InlineData("readback")]
    public async Task ConfirmationRevalidatesTheExactSavedInputAndCallPolicy(string change)
    {
        await using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.Token = cancellation.Token;
        fixture.Preferences.AfterConfirm = () =>
        {
            switch (change)
            {
                case "cancel": cancellation.Cancel(); break;
                case "call": fixture.Call.Set(CallState.Active); break;
                case "readback": fixture.Preferences.Value = null; break;
            }
        };
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<InvalidDataException>();
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Preferences.Pending.Should().BeTrue();
    }

    [Fact]
    public async Task StaleRevisionAndDisposedServiceCannotWrite()
    {
        await using var fixture = new Fixture();
        await FluentActions.Awaiting(() => fixture.Service.SelectAsync(SpeechInputSelection.Default, -1, fixture.Policy.Current.Revision,
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, fixture.Policy, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await fixture.Service.DisposeAsync();
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<ObjectDisposedException>();
        fixture.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ACallTransitionBetweenReadbackAndTheCutoverLockCannotPublish()
    {
        await using var fixture = new Fixture();
        var reads = 0;
        fixture.Preferences.AfterReadBack = () =>
        {
            if (++reads == 2) { fixture.Call.Set(CallState.Active); }
        };
        await FluentActions.Awaiting(() => fixture.Select(SpeechInputSelection.Default)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Runtime.ProfileGeneration.Number.Should().Be(0);
        fixture.Runtime.IsProfileAvailable.Should().BeFalse();
        fixture.Preferences.Pending.Should().BeTrue();
    }

    [Fact]
    public async Task EnabledDiagnosticsRecordStartupCommitFailureAndCancellationWithoutTupleContent()
    {
        var logger = new RecordingLogger();
        await using var invalid = new Fixture(invalidStartup: true, logger: logger);
        await using (var success = new Fixture(logger: logger))
        {
            (await success.Select(SpeechInputSelection.Default)).Should().BeTrue();
            success.Preferences.SaveFailure = true;
            await FluentActions.Awaiting(() => success.Select(SpeechInputSelection.Default)).Should().ThrowAsync<IOException>();
        }
        await using (var cancelled = new Fixture(logger: logger))
        {
            cancelled.Runtime.RetirementFailure = new OperationCanceledException();
            await FluentActions.Awaiting(() => cancelled.Select(SpeechInputSelection.Default)).Should().ThrowAsync<OperationCanceledException>();
        }
        logger.Events.Select(item => item.Name).Should().Contain(["StartupPreferenceUnavailable", "SelectionCommitted", "SelectionFailed", "SelectionCancelled"]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(bool invalidStartup = false, string? startup = null, ILogger<SpeechInputConfigurationService>? logger = null)
        {
            Preferences.Invalid = invalidStartup;
            if (startup is "saved") { Preferences.Value = SpeechInputSelection.Default; }
            if (startup is "unavailable") { Preferences.Value = SpeechInputSelection.Default with { Residency = SpeechInputResidency.KeepLoaded }; }
            if (startup is "malformed") { Preferences.Value = SpeechInputSelection.Default with { ProfileId = "unknown" }; }
            if (startup is "access") { Preferences.ReadFailure = new UnauthorizedAccessException(); }
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Policy = new(Call);
            Service = new(Preferences, Runtime, Admission, Audit, logger ?? NullLogger<SpeechInputConfigurationService>.Instance);
        }
        internal MemoryPreferences Preferences { get; } = new();
        internal Runtime Runtime { get; } = new();
        internal Audit Audit { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy Policy { get; }
        internal SpeechInputConfigurationService Service { get; }
        internal bool Eligible { get; set; } = true;
        internal CancellationToken Token { get; set; } = TestContext.Current.CancellationToken;
        internal Task<bool> Select(SpeechInputSelection selection, RequestOrigin origin = RequestOrigin.LocalUi,
            SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand, long? callRevision = null) =>
            Service.SelectAsync(selection, Service.Get().Revision, callRevision ?? Policy.Current.Revision, origin, initiator,
                Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            await Admission.DisposeAsync();
            Policy.Dispose();
        }
    }

    private sealed class MemoryPreferences : ISpeechInputPreferences
    {
        internal SpeechInputSelection? Value { get; set; }
        internal bool Pending { get; private set; }
        internal bool Invalid { get; set; }
        internal bool SaveFailure { get; set; }
        internal bool ConfirmFailure { get; set; }
        internal bool MismatchReadback { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal Action? AfterReadBack { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal int Writes { get; private set; }
        public SpeechInputSelection? Load() => ReadFailure is { } failure ? throw failure
            : Pending || Invalid ? throw new InvalidDataException("unconfirmed") : Value;
        public SpeechInputSelection? ReadBack()
        {
            AfterReadBack?.Invoke();
            return MismatchReadback && Value is { } saved ? saved with { Resource = SpeechInputResource.Cpu } : Value;
        }
        public void BeginWrite() => Pending = true;
        public void Save(SpeechInputSelection selection)
        {
            if (WriteFailure is { } failure) { throw failure; }
            if (SaveFailure) { throw new IOException("fixture write failure"); }
            Value = selection; Writes++;
        }
        public void ConfirmWrite()
        {
            if (ConfirmFailure) { throw new IOException("fixture confirm failure"); }
            Pending = false;
            AfterConfirm?.Invoke();
        }
    }

    private sealed class Runtime : ISpeechInputProfileRuntime
    {
        public SpeechInputGeneration ProfileGeneration { get; private set; } = new(0, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; private set; }
        internal bool ActiveLease { get; private set; }
        internal int Retirements { get; private set; }
        internal Action? BeforeReturn { get; set; }
        internal Exception? RetirementFailure { get; set; }
        internal void HoldUnavailable() => IsProfileAvailable = false;
        public void InitializeProfile(SpeechInputSelection? selection)
        {
            if (selection is not null) { ProfileGeneration = new(0, selection); }
            IsProfileAvailable = selection is not null && SpeechInputProfileCatalogue.UnavailableReason(selection) is null;
        }
        public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
        {
            HostActivity.RequireCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            Retirements++;
            if (RetirementFailure is { } failure) { IsProfileAvailable = false; throw failure; }
            ActiveLease = true;
            BeforeReturn?.Invoke();
            return Task.FromResult<ISpeechInputProfileSwitch>(new Lease(this));
        }
        private sealed class Lease(Runtime owner) : ISpeechInputProfileSwitch
        {
            public void Publish(SpeechInputGeneration generation)
            {
                owner.ProfileGeneration = generation;
                owner.IsProfileAvailable = SpeechInputProfileCatalogue.UnavailableReason(generation.Selection) is null;
            }
            public void HoldUnavailable() => owner.IsProfileAvailable = false;
            public ValueTask DisposeAsync() { owner.ActiveLease = false; return ValueTask.CompletedTask; }
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest> Requests { get; } = [];
        internal List<ActivityContext> Contexts { get; } = [];
        internal bool FailSuccess { get; set; }
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            BeforeWrite?.Invoke(auditEvent);
            if (FailSuccess && auditEvent.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("fixture terminal audit failure"); }
            Events.Add(auditEvent);
            Requests.Add(HostActivity.RequireCurrent().Request);
            Contexts.Add(Activity.Current!.Context);
        }
    }

    private sealed class Call : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class RecordingLogger : ILogger<SpeechInputConfigurationService>
    {
        internal List<EventId> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Events.Add(eventId);
    }
}