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

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(bool invalidStartup = false)
        {
            Preferences.Invalid = invalidStartup;
            Admission = new(Store, Store, new HostTaskCoordinator(Store));
            Policy = new(Call);
            Service = new(Preferences, Runtime, Admission, Audit, NullLogger<SpeechInputConfigurationService>.Instance);
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
        internal int Writes { get; private set; }
        public SpeechInputSelection? Load() => Pending || Invalid ? throw new InvalidDataException("unconfirmed") : Value;
        public SpeechInputSelection? ReadBack() => MismatchReadback && Value is { } saved ? saved with { Resource = SpeechInputResource.Cpu } : Value;
        public void BeginWrite() => Pending = true;
        public void Save(SpeechInputSelection selection)
        {
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
        public void Write(SecurityAuditEvent auditEvent)
        {
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
}
