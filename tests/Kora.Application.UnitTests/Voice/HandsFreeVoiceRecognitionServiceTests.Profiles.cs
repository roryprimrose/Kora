using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Fact]
    public async Task ProfileSwitchJoinsOwnedWakeRuntimeAndBlocksEveryCaptureRouteUntilLeaseRelease()
    {
        var stage = new Stage();
        var native = new Activated();
        await using var voice = Create(native, stage);
        var dispatches = 0;
        voice.Bind(() => Admission, (_, _) => { dispatches++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var capture = await stage.NextAsync();
        capture.HoldClose = true;
        var switching = voice.RetireProfileAsync(TestContext.Current.CancellationToken);
        await capture.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        switching.IsCompleted.Should().BeFalse();
        voice.IsProfileAvailable.Should().BeFalse();
        native.ProfileRetireCalls.Should().Be(0);
        capture.Binding.CanCapture().Should().BeFalse();
        capture.Result.TrySetResult(new("late command", 1, capture.Binding.Generation));
        capture.ReleaseClose.SetResult();
        await using var lease = await switching;
        voice.IsCaptureQuiescent.Should().BeTrue();
        native.ProfileRetireCalls.Should().Be(1);
        dispatches.Should().Be(0);
        FluentActions.Invoking(() => voice.InitializeProfile(SpeechInputSelection.Default)).Should().Throw<InvalidOperationException>();
        var startWake = () => voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var startPtt = () => voice.BeginPushToTalkAsync(new("mic", "Microphone"), ["help"], cancellationToken: TestContext.Current.CancellationToken);
        var startReply = () => voice.BeginReplyAsync(new("mic", "Microphone"), ["yes"], static () => true, TestContext.Current.CancellationToken);
        await startWake.Should().ThrowAsync<InvalidOperationException>();
        await startPtt.Should().ThrowAsync<InvalidOperationException>();
        await startReply.Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.RetireProfileAsync(TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await voice.ResumeAfterActivatedInputAsync();
        stage.Opens.Should().Be(1);
        native.PushToTalkCalls.Should().Be(0);
        lease.Publish(new(2, SpeechInputSelection.Default));
        voice.IsProfileAvailable.Should().BeFalse();
        await lease.DisposeAsync();
        voice.IsProfileAvailable.Should().BeTrue();
        voice.IsListening.Should().BeFalse();
        await using var second = await voice.RetireProfileAsync(TestContext.Current.CancellationToken);
        await lease.DisposeAsync();
        voice.IsProfileAvailable.Should().BeFalse("repeated disposal cannot release a newer switch");
        second.HoldUnavailable();
        await second.DisposeAsync();
        voice.IsProfileAvailable.Should().BeFalse();
        await startWake.Should().ThrowAsync<InvalidOperationException>();
        await startPtt.Should().ThrowAsync<InvalidOperationException>();
        await startReply.Should().ThrowAsync<InvalidOperationException>();
        await voice.ResumeAfterActivatedInputAsync();
        FluentActions.Invoking(() => second.Publish(new(3, SpeechInputSelection.Default))).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(second.HoldUnavailable).Should().Throw<ObjectDisposedException>();
    }
}
