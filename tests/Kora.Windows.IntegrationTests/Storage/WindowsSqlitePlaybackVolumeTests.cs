using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Voice;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqlitePlaybackVolumeTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task Native_typed_and_activated_gain_use_real_consolidated_session_and_audited_receipts_without_reentrant_lease(
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var observed = new Observed();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new AudioControlAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        using var policy = new CallCommunicationPolicy(new ClearCall());
        var preferences = new LocalPlaybackVolumePreferences(fixture.Paths);
        await using var playback = new OwnedPlayback();
        var volume = new PlaybackVolumeConfigurationService(preferences, playback, admission, audit);
        using var incoming = new Activity("untrusted.volume-correlation").SetIdFormat(ActivityIdFormat.W3C).Start();
        await Apply("0");
        preferences.Load().Should().Be(new PlaybackVolume(0));
        volume.Get().AllowsSpeech.Should().BeFalse();
        var request = observed.Audits.Last().Diagnostic.Host!;
        var before = await fixture.Store.ReadMetadataAsync(request.SessionId, fixture.Token);
        fixture.StageLegacy(2);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        await Apply("1");
        await Apply("100");
        await Apply(null);
        preferences.Load().Should().BeNull();
        playback.Volume.Should().Be(PlaybackVolume.Default);
        playback.SpeechCalls.Should().Be(0);
        (await fixture.Store.ReadMetadataAsync(request.SessionId, fixture.Token)).Authority.Should().Be(before.Authority);
        fixture.Count("work_sessions").Should().Be(1);
        fixture.Count("host_questions").Should().Be(0);
        fixture.Count("scoped_grants").Should().Be(0);
        (await fixture.Tasks.ReadIncompleteAsync(10, fixture.Token)).Should().BeEmpty();
        observed.Gaps.Should().BeEmpty();
        observed.Audits.Should().HaveCount(8).And.OnlyContain(item =>
            item.Diagnostic.Host != null && item.Diagnostic.Host.SessionId == request.SessionId
            && item.Diagnostic.Host.Origin == origin && item.Audit.Initiator == initiator
            && item.Audit.CorrelationId == item.Diagnostic.Host.RequestId.Value);
        observed.Audits.Select(item => item.Audit.Outcome).Should().Equal(
            Enumerable.Repeat(new[] { SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded }, 4).SelectMany(pair => pair));
        HostActivity.Current.Should().BeNull();
        Activity.Current.Should().BeSameAs(incoming);
        new WindowsSqliteEvidenceSink(fixture.Paths).Initialize();

        async Task Apply(string? value)
        {
            await volume.RefreshAsync(origin, static () => true, fixture.Token).WaitAsync(TimeSpan.FromSeconds(15), fixture.Token);
            var proposal = volume.Propose(value, volume.Get().Revision, policy.Current.Revision);
            (await volume.ApplyAsync(proposal, origin, initiator, policy, static () => true, fixture.Token)
                .WaitAsync(TimeSpan.FromSeconds(15), fixture.Token)).Should().BeTrue();
        }
    }

    private sealed class ClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }

    private sealed class OwnedPlayback : ISpeechPlaybackService, IPlaybackVolumeControl
    {
        internal PlaybackVolume? Volume { get; private set; } = PlaybackVolume.Default;
        internal int SpeechCalls => 0;
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void SetPlaybackVolume(PlaybackVolume? volume) => Volume = volume;
        public void InvalidateOutput() { }
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No fixture may synthesize or play audio.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Observed : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "volume-fixture";
        internal List<AuditEnvelope> Audits { get; } = [];
        internal List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) { }
        public void WriteAudit(AuditEnvelope envelope) => Audits.Add(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) { }
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
}
