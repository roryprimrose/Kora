using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteInCallFeedbackTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task Migrated_shared_lease_typed_audits_atomic_preferences_and_restart_preserve_original_session_and_grants(
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var original = await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token);
        fixture.StageLegacy(2);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var observed = new Observed();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new AudioControlAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        using var call = new CallCommunicationPolicy(new ClearCall());
        var preferences = new LocalInCallFeedbackPreferences(fixture.Paths);
        var playback = new Playback();
        var service = new InCallFeedbackConfigurationService(preferences, admission, audit, playback,
            NullLogger<InCallFeedbackConfigurationService>.Instance);
        service.Observe();
        foreach (var mode in Enum.GetValues<InCallFeedbackMode>())
        {
            await service.RefreshAsync(origin, call.Current.Revision, static () => true, fixture.Token);
            (await service.SelectAsync(service.Choices.Single(choice => choice.Mode == mode), false, call.Current.Revision,
                origin, initiator, call, static () => true, fixture.Token)).Should().BeTrue();
            preferences.Load().Should().Be(mode);
            var cold = new InCallFeedbackConfigurationService(new LocalInCallFeedbackPreferences(fixture.Paths),
                admission, audit, playback, NullLogger<InCallFeedbackConfigurationService>.Instance);
            cold.Observe();
            cold.Get().Desired.Should().Be(mode);
        }
        await service.RefreshAsync(origin, call.Current.Revision, static () => true, fixture.Token);
        await service.SelectAsync(service.Choices.Single(choice => choice.Mode == InCallFeedbackMode.UI), true,
            call.Current.Revision, origin, initiator, call, static () => true, fixture.Token);
        preferences.Load().Should().BeNull();
        service.Get().Source.Should().Be("default");
        observed.Audits.Should().HaveCount(10).And.OnlyContain(item => item.Audit.ActionId == "configuration.in-call-feedback"
            && item.Audit.Initiator == initiator && item.Diagnostic.Host!.Origin == origin
            && item.Audit.CorrelationId == item.Diagnostic.Host.RequestId.Value);
        var controlSession = observed.Audits[0].Diagnostic.Host!.SessionId;
        controlSession.Should().NotBe(fixture.Request.SessionId);
        observed.Audits.Should().OnlyContain(item => item.Diagnostic.Host!.SessionId == controlSession);
        observed.Gaps.Should().BeEmpty();
        (await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(original);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Contain(grant);
        (await fixture.Tasks.ReadIncompleteAsync(100, fixture.Token)).Should().BeEmpty();
        fixture.Count("host_questions").Should().Be(1);
        playback.Invalidations.Should().BeGreaterThan(0);
        preferences.BeginWrite();
        preferences.Save(InCallFeedbackMode.Voice);
        FluentActions.Invoking(service.Observe).Should().Throw<InvalidDataException>();
        service.Get().Available.Should().BeFalse();
        FluentActions.Invoking(() => new LocalInCallFeedbackPreferences(fixture.Paths).Load()).Should().Throw<InvalidDataException>();
    }

    private sealed class Observed : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "call-feedback-fixture";
        internal List<AuditEnvelope> Audits { get; } = [];
        internal List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) { }
        public void WriteActivity(CompletedActivityEnvelope envelope) { }
        public void WriteAudit(AuditEnvelope envelope) => Audits.Add(envelope);
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
    private sealed class ClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
    private sealed class Playback : ISpeechPlaybackService
    {
        internal int Invalidations { get; private set; }
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() => Invalidations++;
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Configuration must never speak.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
