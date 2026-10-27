using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NativeQuestionWindowContractTests
{
    [Theory]
    [InlineData("Show local version")]
    [InlineData("Nova Prime Show local version")]
    [InlineData("nova prime, 1")]
    [InlineData("Nova Prime option 1")]
    public async Task Current_name_or_bare_choice_completes_exact_native_question_with_truthful_voice_outcome(string transcript)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            var model = await CreateModelAsync(fixture, static () => true);
            var key = model.Key;
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture();
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var replies = new HostQuestionVoiceReplyService(fixture.Questions, capture, fixture.Time,
                new ReplyUiDispatcher(),
                () => new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1) { ActivationName = "Nova Prime" },
                (_, _, _) => Task.CompletedTask, NullLogger<HostQuestionVoiceReplyService>.Instance);
            model.BindVoiceReplies(replies, static () => true);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                await model.AnswerByVoiceAsync();
                native.DetectSpeech();
                native.Transcript(transcript);
                var decision = await model.Completion.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
                await replies.Quiescence;
                decision.Outcome.Should().Be(HostInteractionOutcome.Answered);
                model.Question.Key.Should().Be(key.Next());
                model.Question.Draft!.Choices.Should().Equal("show");
                model.Question.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
                var saved = (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single();
                saved.Key.Should().Be(model.Question.Key);
                saved.Draft!.Choices.Should().Equal("show");
                saved.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
                var status = window.FindControl<TextBlock>("QuestionStatus")!;
                status.Text.Should().Contain(model.Status);
                ControlAutomationPeer.CreatePeerForElement(status)!.GetName().Should().Contain(model.Status);
                model.IsEditable.Should().BeFalse();
                capture.IsCaptureQuiescent.Should().BeTrue();
                native.Disposed.Task.IsCompletedSuccessfully.Should().BeTrue();
                native.ReleaseCount.Should().BeGreaterThan(0);
                factory.OpenCount.Should().Be(1);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("name")]
    [InlineData("window")]
    public async Task Named_capture_revocation_fences_ignoring_callbacks_and_joins_native_disposal_before_typed_answer(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            var model = await CreateModelAsync(fixture, static () => true);
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture
            {
                DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            var admission = new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1) { ActivationName = "Nova" };
            await using var replies = new HostQuestionVoiceReplyService(fixture.Questions, capture, fixture.Time,
                new ReplyUiDispatcher(), () => admission, (_, _, _) => Task.CompletedTask,
                NullLogger<HostQuestionVoiceReplyService>.Instance);
            model.BindVoiceReplies(replies, static () => true);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                await model.AnswerByVoiceAsync();
                var late = native.QueueTranscript("Nova Show local version");
                if (boundary is "name")
                {
                    admission = admission with { ActivationName = "Atlas" };
                    replies.Revalidate();
                }
                else { window.Close(); }
                await native.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
                capture.IsListening.Should().BeFalse();
                replies.IsActive.Should().BeFalse();
                replies.Quiescence.IsCompleted.Should().BeFalse();
                late();
                if (boundary is "name") { model.Completion.IsCompleted.Should().BeFalse(); }
                else { (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied); }
                native.DisposeGate.SetResult();
                await replies.Quiescence;
                capture.IsCaptureQuiescent.Should().BeTrue();
                late();
                (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Status
                    .Should().Be(QuestionStatus.Pending);
                factory.OpenCount.Should().Be(1);
                if (boundary is "name")
                {
                    model.Edit(new(["show"]));
                    await model.SubmitAsync();
                    var decision = await model.Completion;
                    decision.Outcome.Should().Be(HostInteractionOutcome.Answered);
                    decision.Question!.AnswerChannel.Should().Be(RequestOrigin.LocalUi);
                    var saved = (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single();
                    saved.AnswerChannel.Should().Be(RequestOrigin.LocalUi);
                    saved.Draft!.Choices.Should().Equal("show");
                    factory.OpenCount.Should().Be(1);
                }
            }
            finally
            {
                native.DisposeGate.TrySetResult();
                window.Close();
            }
        });
    }
}
