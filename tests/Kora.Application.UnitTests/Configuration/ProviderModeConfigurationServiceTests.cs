using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ProviderModeConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ProviderModeConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalFirst)]
    [InlineData(ModelProviderMode.HostedPreferred)]
    public async Task DefaultSaveRestartAndResetHaveTruthfulProvenance(ModelProviderMode mode)
    {
        await using var f = new Fixture();
        f.Service.Get().Available.Should().BeFalse();
        f.Service.Observe();
        f.Service.Get().Saved.Should().BeNull();
        f.Service.Get().Desired.Should().Be(ModelProviderMode.LocalOnly);
        f.Service.Get().Source.Should().Be("default");
        await f.Refresh();
        f.Service.Choices.Select(choice => choice.Mode).Should().Equal(ModelProviderModePreference.Choices);
        var revision = f.Service.Get().Revision;
        (await f.Select(mode)).Should().BeTrue();
        f.Service.Get().Revision.Should().BeGreaterThan(revision);
        f.Service.Get().Source.Should().Be("saved");
        var restarted = f.CreateService();
        restarted.Observe();
        restarted.Get().Desired.Should().Be(mode);
        await f.Refresh();
        await f.Select(ModelProviderMode.LocalOnly);
        f.Service.Get().Saved.Should().Be(ModelProviderMode.LocalOnly);
        f.Preferences.Pending.Should().BeFalse();
        f.Audit.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        f.Store.Tasks.Should().Contain(task => task.State == HostTaskState.Succeeded);
        f.Audit.Hosts.Should().OnlyContain(host => host != null && host.SessionId == f.Store.Authority!.SessionId);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("stale")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("host")]
    [InlineData("channel")]
    [InlineData("saved")]
    [InlineData("hold")]
    [InlineData("disposed")]
    public async Task ForeignStaleAndChangedChoicesNeverWrite(string stage)
    {
        await using var f = new Fixture();
        await f.Refresh();
        var choice = f.Service.Choices[1];
        if (stage is "foreign")
        {
            var other = f.CreateService();
            await other.RefreshAsync(RequestOrigin.LocalUi, static () => true, f.Token);
            choice = other.Choices[1];
        }
        if (stage is "lookalike") { choice = new(choice.Mode, choice.Revision, choice.Owner, choice.Session, choice.Origin, choice.RemainsAdmitted); }
        if (stage is "stale") { await f.Refresh(); }
        if (stage is "session") { f.Store.Authority = f.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (stage is "generation") { f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        if (stage is "host") { f.Eligible = false; }
        if (stage is "saved") { f.Preferences.Mode = ModelProviderMode.HostedPreferred; }
        if (stage is "hold") { f.Service.HoldUnavailable(); }
        if (stage is "disposed") { await f.Admission.DisposeAsync(); }
        await FluentActions.Awaiting(() => f.Apply(choice, stage is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("readback")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("receipt")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-host")]
    [InlineData("confirmed-revision")]
    public async Task FailedEvidenceNeverActivatesAndInterruptedWritesRemainUnavailableAcrossRestart(string stage)
    {
        await using var f = new Fixture();
        await f.Refresh();
        f.Preferences.SaveFailure = stage is "io" ? new IOException() : null;
        f.Preferences.AfterWrite = () => { if (stage is "readback") { f.Preferences.Mode = ModelProviderMode.LocalOnly; } };
        f.Preferences.ConfirmFailure = stage is "confirm" ? new IOException() : null;
        f.Preferences.AfterConfirm = () =>
        {
            if (stage is "confirmed-readback") { f.Preferences.Mode = ModelProviderMode.LocalOnly; }
            if (stage is "confirmed-host") { f.Eligible = false; }
            if (stage is "confirmed-revision") { f.Service.HoldUnavailable(); }
        };
        f.Store.FailTerminal = stage is "receipt";
        f.Audit.BeforeWrite = item =>
        {
            if (stage is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || stage is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        await FluentActions.Awaiting(() => f.Select(ModelProviderMode.LocalFirst)).Should().ThrowAsync<Exception>();
        f.Service.Get().Available.Should().BeFalse();
        f.Service.Choices.Should().BeEmpty();
        if (stage is not "requested-audit")
        {
            f.Preferences.Pending.Should().BeTrue();
            f.CreateService().Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidSavedStateIsNotADefault(bool refresh)
    {
        await using var f = new Fixture();
        f.Preferences.LoadFailure = new InvalidDataException();
        Func<Task> read = refresh ? f.Refresh : () => { f.Service.Observe(); return Task.CompletedTask; };
        await read.Should().ThrowAsync<InvalidDataException>();
        f.Service.Get().Source.Should().Be("unavailable");
        f.Service.Get().Desired.Should().BeNull();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("revision")]
    [InlineData("readback")]
    public async Task TerminalReceiptRacesDoNotConfirmState(string stage)
    {
        await using var f = new Fixture();
        await f.Refresh();
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (stage is "host") { f.Eligible = false; }
            if (stage is "revision") { f.Service.HoldUnavailable(); }
            if (stage is "readback") { f.Preferences.Mode = ModelProviderMode.HostedPreferred; }
        };
        await FluentActions.Awaiting(() => f.Select(ModelProviderMode.LocalFirst)).Should().ThrowAsync<Exception>();
        f.Preferences.Pending.Should().BeTrue();
        f.Service.Get().Available.Should().BeFalse();
    }

    [Fact]
    public async Task AuditReentrancyAndHostRevocationCannotCommit()
    {
        await using var f = new Fixture();
        await f.Refresh();
        f.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Requested) { f.Eligible = false; } };
        (await f.Select(ModelProviderMode.LocalFirst)).Should().BeFalse();
        f.Eligible = true;
        f.Audit.BeforeWrite = null;
        await f.Refresh();
        f.Audit.BeforeWrite = _ => f.Service.Observe();
        await FluentActions.Awaiting(() => f.Select(ModelProviderMode.LocalFirst)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProtectedVoiceStaleCallModelOriginAndCancellationNeverWrite()
    {
        await using var f = new Fixture();
        f.Call.State = CallState.Unknown;
        await f.Service.RefreshAsync(RequestOrigin.ActivatedVoice, () => f.Eligible, f.Token);
        (await f.Apply(f.Service.Choices[1], RequestOrigin.ActivatedVoice, initiator: SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        await f.Refresh();
        (await f.Apply(f.Service.Choices[1], callRevision: 99)).Should().BeFalse();
        await FluentActions.Awaiting(() => f.Apply(f.Service.Choices[1], initiator: SecurityAuditInitiator.VoiceCommand)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => f.Apply(f.Service.Choices[1], initiator: SecurityAuditInitiator.ModelSuggestion)).Should().ThrowAsync<InvalidOperationException>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await FluentActions.Awaiting(() => f.Service.SelectAsync(f.Service.Choices[1], f.Policy.Current.Revision, RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, f.Policy, static () => true, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        f.Preferences.Writes.Should().Be(0);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture() { Admission = new(Store, Store, new HostTaskCoordinator(Store)); Policy = new(Call); Service = CreateService(); }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal bool Eligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal Audit Audit { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal Call Call { get; } = new();
        internal CallCommunicationPolicy Policy { get; }
        internal ProviderModeConfigurationService Service { get; }
        internal ProviderModeConfigurationService CreateService() => new(Preferences, Admission, Audit);
        internal Task Refresh() => Service.RefreshAsync(RequestOrigin.LocalUi, () => Eligible, Token);
        internal Task<bool> Select(ModelProviderMode mode) => Apply(Service.Choices.Single(choice => choice.Mode == mode));
        internal Task<bool> Apply(ProviderModeChoice choice, RequestOrigin origin = RequestOrigin.LocalUi,
            long? callRevision = null, SecurityAuditInitiator initiator = SecurityAuditInitiator.TypedCommand) =>
            Service.SelectAsync(choice, callRevision ?? Policy.Current.Revision, origin, initiator, Policy, () => Eligible, Token);
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }
    private sealed class Preferences : IModelProviderModePreferences
    {
        internal bool Pending { get; set; }
        internal ModelProviderMode? Mode { get; set; }
        internal Exception? LoadFailure { get; set; }
        internal Exception? SaveFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterConfirm { get; set; }
        internal int Writes { get; private set; }
        public ModelProviderMode? Load() => Pending ? throw new InvalidDataException("Unconfirmed write") : ReadBack();
        public ModelProviderMode? ReadBack() { if (LoadFailure is { } error) { throw error; } return Mode; }
        public void BeginWrite() => Pending = true;
        public void ConfirmWrite() { if (ConfirmFailure is { } error) { throw error; } Pending = false; AfterConfirm?.Invoke(); }
        public void Save(ModelProviderMode mode) { if (SaveFailure is { } error) { throw error; } Mode = mode; Writes++; AfterWrite?.Invoke(); }
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
}
