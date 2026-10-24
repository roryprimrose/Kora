using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class HandsFreeQuestionVoiceTests
{
    [Theory]
    [InlineData("answer")]
    [InlineData("timeout")]
    [InlineData("failure")]
    [InlineData("withdrawal")]
    public async Task Exact_prefix_free_question_pauses_wake_and_only_fresh_successful_retirement_can_rearm(string ending)
    {
        var audio = new QuietWakeAudioStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        await using var fixture = new QuestionReplyFixture(capture =>
            decorated = new(capture, audio, NullLogger<HandsFreeVoiceRecognitionService>.Instance));
        var voice = decorated ?? throw new InvalidOperationException("Expected the decorated host.");
        voice.Bind(() => fixture.Admission is { } admitted
                ? new(admitted.Microphone, "Kora", new(1, SpeechInputSelection.Default),
                    admitted.RecoveryRevision, admitted.TopologyRevision, admitted.CallRevision, 1)
                : null,
            (_, _) => throw new InvalidOperationException("Ambient audio must not become a question answer."),
            () => !fixture.Replies.IsActive);
        var question = await fixture.Host.QuestionAsync();
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        audio.Opens.Should().Be(1);
        await fixture.OpenAsync(question);
        audio.Closures.Should().Be(1);
        fixture.Capture.Opens.Should().Be(1);
        fixture.Replies.IsActive.Should().BeTrue();
        switch (ending)
        {
            case "answer":
                fixture.Capture.Speech();
                fixture.Capture.Transcript("First");
                break;
            case "timeout":
                fixture.Time.Advance(TimeSpan.FromSeconds(16));
                break;
            case "failure":
                fixture.Capture.Fail();
                break;
            case "withdrawal":
                fixture.Admission = null;
                fixture.Replies.Revalidate();
                break;
        }
        await fixture.Replies.Quiescence;
        fixture.Replies.IsActive.Should().BeFalse();
        if (ending is "answer")
        {
            fixture.Accepted.Should().NotBeNull();
            fixture.Accepted!.Question!.Key.QuestionId.Should().Be(question.Key.QuestionId);
            fixture.Accepted.Question.Key.Request.Should().Be(question.Key.Request);
            fixture.Accepted.Question.Key.Revision.Value.Should().Be(question.Key.Revision.Value + 1);
        }
        else { fixture.Accepted.Should().BeNull(); }
        audio.Opens.Should().Be(ending is "answer" or "timeout" ? 2 : 1);
        if (ending is "failure") { voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired); }
    }

    private sealed class QuietWakeAudioStage : IHandsFreeAudioStage
    {
        public bool IsAvailable => true;
        public int Opens { get; private set; }
        public int Closures { get; private set; }
        public Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken cancellationToken)
        {
            Opens++;
            return Task.FromResult<IHandsFreeAudioSession>(new QuietSession(() => Closures++));
        }

        private sealed class QuietSession(Action closed) : IHandsFreeAudioSession
        {
            private readonly TaskCompletionSource<WakeWordObservation> noWake = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool IsQuiescent { get; private set; }
            public Task<WakeWordObservation> WaitForWakeAsync(CancellationToken cancellationToken) => noWake.Task.WaitAsync(cancellationToken);
            public Task<VoiceTranscriptEventArgs?> CaptureCommandAsync(WakeWordObservation wake, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("No ambient transcription is admitted.");
            public void Invalidate() { }
            public ValueTask DisposeAsync()
            {
                closed();
                IsQuiescent = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
