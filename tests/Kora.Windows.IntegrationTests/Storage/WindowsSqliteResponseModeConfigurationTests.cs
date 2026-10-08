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

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Host tracing")]
public sealed class WindowsSqliteResponseModeConfigurationTests
{
    [Fact]
    public async Task Real_atomic_preferences_and_migrated_shared_session_lease_commit_without_reentrant_authority_or_new_grants()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        fixture.StageLegacy(2);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        await using var admission = new AudioControlAdmission(fixture.Store, fixture.Store, new HostTaskCoordinator(fixture.Tasks));
        using var call = new CallCommunicationPolicy(new Call());
        var preferences = new LocalResponseOutputPreferences(fixture.Paths, NullLogger<LocalResponseOutputPreferences>.Instance);
        preferences.SaveMutedOutputVisualFallback(false);
        var playback = new Playback();
        var audit = new Audit();
        var service = new ResponseModeConfigurationService(preferences, admission, audit, playback);
        service.Observe();
        await service.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
        await service.SelectAsync(service.Choices.Single(choice => choice.Mode == ResponseOutputMode.VoiceOnly),
            call.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, call, static () => true, fixture.Token);
        preferences.LoadDefaultMode().Should().Be(ResponseOutputMode.VoiceOnly);
        preferences.LoadMutedOutputVisualFallback().Should().BeFalse();
        service.Get().Source.Should().Be("saved");
        var restarted = new ResponseModeConfigurationService(
            new LocalResponseOutputPreferences(fixture.Paths, NullLogger<LocalResponseOutputPreferences>.Instance), admission, audit, playback);
        restarted.Observe();
        restarted.Get().Desired.Should().Be(ResponseOutputMode.VoiceOnly);
        await restarted.RefreshAsync(RequestOrigin.LocalUi, static () => true, fixture.Token);
        await restarted.SelectAsync(restarted.Choices.Single(choice => choice.Mode == ResponseOutputMode.Hybrid),
            call.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, call, static () => true, fixture.Token);
        restarted.Get().Saved.Should().Be(ResponseOutputMode.Hybrid);
        audit.Requests.Should().HaveCount(4).And.OnlyContain(request => request.Origin == RequestOrigin.LocalUi);
        audit.Requests[0].Should().BeSameAs(audit.Requests[1]);
        audit.Requests[2].Should().BeSameAs(audit.Requests[3]);
        var metadata = await fixture.Store.ReadMetadataAsync(audit.Requests[0].SessionId, fixture.Token);
        metadata.Authority.IsActive.Should().BeTrue();
        fixture.Count("host_questions").Should().Be(0);
        fixture.Count("scoped_grants").Should().Be(0);
        playback.Invalidations.Should().BeGreaterThan(0);
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<HostRequest> Requests { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Requests.Add(HostActivity.RequireCurrent().Request);
    }
    private sealed class Call : ICallStateService
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
