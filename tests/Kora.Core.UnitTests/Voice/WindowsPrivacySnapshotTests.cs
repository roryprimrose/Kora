using AwesomeAssertions;

using Kora.Core.Platform;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class WindowsPrivacySnapshotTests
{
    [Theory]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    [InlineData(WindowsSessionState.Suspended)]
    [InlineData(WindowsSessionState.SignedOut)]
    public void Unconfirmed_or_ineligible_session_cannot_capture(WindowsSessionState state)
    {
        var snapshot = new WindowsPrivacySnapshot(
            state, MicrophoneAccessState.Allowed, 1, ["mic"], "mic", "speaker");

        snapshot.CanCapture.Should().BeFalse();
        snapshot.CanCaptureFrom(new MicrophoneDevice("mic", "test")).Should().BeFalse();
    }

    [Fact]
    public void System_selection_resolves_only_the_observed_default_not_a_same_name_endpoint()
    {
        var snapshot = new WindowsPrivacySnapshot(
            WindowsSessionState.Unlocked, MicrophoneAccessState.Allowed, 1, ["mic"], "missing", "speaker");

        snapshot.CanCaptureFrom(new MicrophoneDevice("system", "System", IsSystemDefault: true)).Should().BeFalse();
        snapshot.CanCaptureFrom(new MicrophoneDevice("mic", "Duplicate name")).Should().BeTrue();
        snapshot.CanCaptureFrom(new MicrophoneDevice("missing", "Duplicate name")).Should().BeFalse();
    }

    [Fact]
    public void Voice_results_and_failures_preserve_the_capture_generation()
    {
        new VoiceTranscriptEventArgs("help", 0.8f, 42).Generation.Should().Be(42);
        new VoiceRecognitionFailureEventArgs("unavailable", 42).Generation.Should().Be(42);
    }

    [Fact]
    public void Privacy_changes_preserve_their_observation_reason()
    {
        var snapshot = new WindowsPrivacySnapshot(
            WindowsSessionState.Unlocked, MicrophoneAccessState.Allowed, 1, ["mic"], "mic", "speaker");
        var reason = WindowsPrivacyChangeReason.OutputTopology | WindowsPrivacyChangeReason.DefaultSpeaker;

        new WindowsPrivacyChangedEventArgs(snapshot, snapshot, reason).Reason.Should().Be(reason);
    }

    [Fact]
    public void Recorder_closure_can_preserve_a_valid_final_transcript_generation()
    {
        var state = new VoiceCaptureStateChangedEventArgs(42, isListening: false);

        state.Generation.Should().Be(42);
        state.IsListening.Should().BeFalse();
    }

    [Fact]
    public void Completion_reports_the_retired_activation_and_its_non_failure_reason()
    {
        var result = new VoiceRecognitionCompletedEventArgs(42, VoiceRecognitionCompletionReason.EmptySpeechTimeout);

        result.Generation.Should().Be(42);
        result.Reason.Should().Be(VoiceRecognitionCompletionReason.EmptySpeechTimeout);
    }
}
