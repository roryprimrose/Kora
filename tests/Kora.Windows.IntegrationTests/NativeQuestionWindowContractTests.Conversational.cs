using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Interaction;
using Kora.Application.Voice;
using Kora.Core.Interaction;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NativeQuestionWindowContractTests
{
    [Theory]
    [InlineData("answer")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("preference")]
    [InlineData("duplicate")]
    public async Task New_foreground_question_speaks_exactly_then_captures_once_without_click_and_never_rearms(string ending)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            var model = await CreateModelAsync(fixture, static () => true);
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture();
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            var playback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var spoken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            capture.CaptureStateChanged += (_, args) => { if (capture.IsListening) { listening.TrySetResult(); } };
            ConversationalReplyState? preference = new(true, 1);
            var time = new ReplyUiTime { Now = fixture.Time.Now };
            var speechCount = 0;
            await using var replies = new HostQuestionVoiceReplyService(fixture.Questions, capture, time, new ReplyUiDispatcher(),
                () => new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1), async (text, eligible, token) =>
                {
                    speechCount++;
                    text.Should().Be(model.Question.Spec.Text);
                    eligible().Should().BeTrue();
                    capture.IsListening.Should().BeFalse();
                    spoken.TrySetResult();
                    await playback.Task.WaitAsync(token);
                }, NullLogger<HostQuestionVoiceReplyService>.Instance, conversationalPreference: () => preference);
            model.BindVoiceReplies(replies, static () => true);
            var window = CreateWindow(model);
            QuestionWindow? duplicate = null;
            try
            {
                await ShowAsync(window, model);
                await spoken.Task.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
                factory.OpenCount.Should().Be(0);
                playback.SetResult();
                await listening.Task.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
                model.VoiceReplyStatus.Should().Contain("15 seconds");
                var late = native.QueueTranscript("Show local version");
                switch (ending)
                {
                    case "answer": native.Transcript("Show local version"); break;
                    case "cancel":
                        window.FindControl<Button>("CancelReply")!.Focus().Should().BeTrue();
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); break;
                    case "timeout": time.Now += TimeSpan.FromSeconds(15); time.Timer!.Tick(); break;
                    case "preference": preference = new(true, 2); replies.Revalidate(); break;
                    case "duplicate": duplicate = CreateWindow(model); await ShowAsync(duplicate, model); break;
                }
                await replies.Quiescence;
                late();
                await model.TryConversationalReplyAsync();
                speechCount.Should().Be(1);
                factory.OpenCount.Should().Be(1);
                capture.IsListening.Should().BeFalse();
                if (ending is "answer")
                {
                    (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Answered);
                    model.Question.Draft!.Choices.Should().Equal("show");
                }
                else
                {
                    model.Completion.IsCompleted.Should().BeFalse();
                    model.IsEditable.Should().BeTrue();
                }
            }
            finally { duplicate?.Close(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Visual_only_or_competing_presenter_never_starts_automatic_playback_or_capture(bool competing)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            var model = await CreateModelAsync(fixture, static () => true);
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            factory.OpenResult.SetResult(new ActivatedVoiceRecognitionTests.FakeCapture());
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var replies = new HostQuestionVoiceReplyService(fixture.Questions, capture, fixture.Time, new ReplyUiDispatcher(),
                () => new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1),
                (_, _, _) => throw new InvalidOperationException("No automatic playback admitted"),
                NullLogger<HostQuestionVoiceReplyService>.Instance,
                conversationalPreference: () => new(competing, 1));
            model.BindVoiceReplies(replies, static () => true);
            if (competing) { model.BindVisibleVoicePresenter(static () => true); }
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                await model.TryConversationalReplyAsync();
                factory.OpenCount.Should().Be(0);
                model.IsEditable.Should().BeTrue();
                if (!competing)
                {
                    await model.AnswerByVoiceAsync();
                    factory.OpenCount.Should().Be(1);
                    model.StopVoiceReply();
                    await replies.Quiescence;
                }
            }
            finally { window.Close(); }
        });
    }
}
