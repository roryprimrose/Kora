using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using AwesomeAssertions;
using Kora.Application;
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
    [Fact]
    public async Task Exact_voice_controls_have_accessible_state_countdown_cancel_and_timeout_preserves_native_question()
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
            var time = new ReplyUiTime { Now = fixture.Time.Now };
            await using var replies = new HostQuestionVoiceReplyService(fixture.Questions, capture, time,
                new ReplyUiDispatcher(), () => new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1),
                (_, _, _) => Task.CompletedTask, NullLogger<HostQuestionVoiceReplyService>.Instance);
            model.BindVoiceReplies(replies, static () => true);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                var answer = window.FindControl<Button>("AnswerByVoice")!;
                var cancel = window.FindControl<Button>("CancelReply")!;
                var status = window.FindControl<TextBlock>("VoiceReplyStatus")!;
                ControlAutomationPeer.CreatePeerForElement(answer)!.GetName().Should().Contain("exact foreground question");
                ControlAutomationPeer.CreatePeerForElement(cancel)!.GetName().Should().Contain("close microphone");
                answer.IsEnabled.Should().BeTrue();
                answer.Focus().Should().BeTrue();
                await model.AnswerByVoiceAsync();
                capture.IsListening.Should().BeTrue();
                status.Text.Should().Contain("Awaiting reply").And.Contain("15 seconds");
                ControlAutomationPeer.CreatePeerForElement(status)!.GetName().Should().Contain("Awaiting reply");
                cancel.IsEnabled.Should().BeTrue();
                cancel.Focus().Should().BeTrue();
                time.Now += TimeSpan.FromSeconds(15);
                time.Timer!.Tick();
                await replies.Quiescence;
                capture.IsListening.Should().BeFalse();
                model.IsEditable.Should().BeTrue();
                model.Completion.IsCompleted.Should().BeFalse();
                status.Text.Should().Contain("timed out").And.Contain("question remains answerable");
                window.FindControl<Button>("CancelQuestion")!.IsEnabled.Should().BeTrue();
                window.FocusManager!.GetFocusedElement().Should().NotBeSameAs(answer);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Native_cancel_and_duplicate_presenters_stop_capture_without_cancelling_or_retargeting_question()
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
            await using var replies = new HostQuestionVoiceReplyService(fixture.Questions, capture, fixture.Time,
                new ReplyUiDispatcher(), () => new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1),
                (_, _, _) => Task.CompletedTask, NullLogger<HostQuestionVoiceReplyService>.Instance);
            model.BindVoiceReplies(replies, static () => true);
            var window = CreateWindow(model);
            QuestionWindow? duplicate = null;
            try
            {
                await ShowAsync(window, model);
                await model.AnswerByVoiceAsync();
                var old = native.QueueTranscript("Show local version");
                var cancel = window.FindControl<Button>("CancelReply")!;
                cancel.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                capture.IsListening.Should().BeFalse();
                old();
                await replies.Quiescence;
                model.Completion.IsCompleted.Should().BeFalse();
                duplicate = CreateWindow(model);
                duplicate.Show();
                duplicate.Activate();
                model.CanAnswerByVoice.Should().BeFalse();
                await model.AnswerByVoiceAsync();
                factory.OpenCount.Should().Be(1);
                (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
            }
            finally { duplicate?.Close(); window.Close(); }
        });
    }

    private sealed class ReplyUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
    }

    private sealed class ReplyUiTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public ReplyUiTimer? Timer { get; private set; }
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer = new(() => callback(state));
            return Timer;
        }
        internal sealed class ReplyUiTimer(Action tick) : ITimer
        {
            public void Tick() => tick();
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
