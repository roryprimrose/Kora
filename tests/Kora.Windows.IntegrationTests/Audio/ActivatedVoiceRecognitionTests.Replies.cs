using AwesomeAssertions;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Session;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed partial class ActivatedVoiceRecognitionTests
{
    [Fact]
    public async Task Reply_uses_fifteen_second_start_window_and_single_utterance_completion_without_button_release()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var native = new FakeCapture();
        factory.OpenResult.SetResult(native);
        var time = new PrivacyPollingTimeProvider();
        await using var service = new WindowsVoiceRecognitionService(Logger, privacy, factory, VoiceCaptureLimits.Default, time);
        var speech = new List<long>();
        var transcripts = new List<VoiceTranscriptEventArgs>();
        service.ReplySpeechStarted += (_, args) => speech.Add(args.Generation);
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        await service.BeginReplyAsync(Microphone, ["Yes", "No"], static () => true, TestContext.Current.CancellationToken);
        factory.SpeechStartWindow.Should().Be(TimeSpan.FromSeconds(15));
        time.Timer.DueTime.Should().Be(TimeSpan.FromSeconds(15));
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        native.DetectSpeech();
        speech.Should().Equal(service.Generation);
        time.Timer.IsDisposed.Should().BeTrue();
        native.Transcript("Yes");
        await service.StopAsync(TestContext.Current.CancellationToken);
        transcripts.Should().ContainSingle().Which.Transcript.Should().Be("Yes");
        factory.Stream!.BufferedBytes.Should().Be(0);
        service.IsCaptureQuiescent.Should().BeTrue();
        service.IsAmbientListeningAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Host_denial_prevents_reply_open_and_admission_loss_before_native_start_prevents_recording()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        await using var service = Create(privacy, factory);
        var denied = () => service.BeginReplyAsync(Microphone, ["Yes"], static () => false, TestContext.Current.CancellationToken);
        await denied.Should().ThrowAsync<InvalidOperationException>();
        factory.OpenCount.Should().Be(0);
        var native = new FakeCapture();
        factory.OpenResult.SetResult(native);
        var eligible = true;
        privacy.BeforeRefresh = () => { if (factory.OpenCount > 0) { eligible = false; } };
        var opening = () => service.BeginReplyAsync(Microphone, ["Yes"], () => eligible, TestContext.Current.CancellationToken);
        await opening.Should().ThrowAsync<InvalidOperationException>();
        native.StartCount.Should().Be(0);
        service.IsListening.Should().BeFalse();
    }

    [Theory]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    public async Task Reply_session_revocation_is_synchronous_and_delayed_transcript_is_fenced(WindowsSessionState state)
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var native = new FakeCapture();
        factory.OpenResult.SetResult(native);
        await using var service = Create(privacy, factory);
        var accepted = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => accepted.Add(args);
        await service.BeginReplyAsync(Microphone, ["Yes"], static () => true, TestContext.Current.CancellationToken);
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        var delayed = native.QueueTranscript("Yes");
        native.Audio([1, 2, 3]);
        privacy.Set(Ready with { SessionState = state });
        service.IsListening.Should().BeFalse();
        factory.Stream!.BufferedBytes.Should().Be(0);
        delayed();
        accepted.Should().BeEmpty();
    }

    [Fact]
    public async Task Reply_default_and_topology_changes_retire_instead_of_rerouting_and_empty_timeout_is_bounded()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var native = new FakeCapture();
        factory.OpenResult.SetResult(native);
        var time = new PrivacyPollingTimeProvider();
        await using var service = new WindowsVoiceRecognitionService(Logger, privacy, factory, VoiceCaptureLimits.Default, time);
        await service.BeginReplyAsync(Microphone, ["Yes"], static () => true, TestContext.Current.CancellationToken);
        privacy.Set(Ready with { TopologyRevision = 1, DefaultMicrophoneId = "other" });
        service.IsListening.Should().BeFalse();
        await service.StopAsync(TestContext.Current.CancellationToken);
        factory.OpenResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.OpenResult.SetResult(new FakeCapture());
        await service.BeginReplyAsync(Microphone, ["Yes"], static () => true, TestContext.Current.CancellationToken);
        time.Timer.Tick();
        service.IsListening.Should().BeFalse();
        await service.StopAsync(TestContext.Current.CancellationToken);
        factory.Stream!.BufferedBytes.Should().Be(0);
    }
}
