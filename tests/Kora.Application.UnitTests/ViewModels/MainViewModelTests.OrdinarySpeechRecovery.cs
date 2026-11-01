using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static async Task StopOrdinarySpeechAsync(Fixture fixture, string route)
    {
        if (route is "native") { await fixture.ViewModel.StopSpeechCommand.ExecuteAsync(); }
        else if (route is "tray") { await fixture.ViewModel.StopSpeakingFromTrayAsync(); }
        else if (route is "voice") { await fixture.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1); }
        else { await fixture.RunAsync("stop speaking"); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinarySpeechRecoveryKeepsStopAvailableWhenProviderReturnsActive(bool preview)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ReturnWhileSpeaking = true;
        var stops = f.TextToSpeech.StopCalls;
        if (preview) { await f.ViewModel.PreviewVoiceCommand.ExecuteAsync(); }
        else { await f.RunAsync("what power action is pending"); }

        f.TextToSpeech.IsSpeaking.Should().BeTrue();
        f.ViewModel.IsSpeaking.Should().BeTrue();
        f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
        f.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.CanReadCurrentResponse.Should().BeFalse();
        f.TextToSpeech.StopCalls.Should().Be(stops);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.ViewModel.ResponseOutputStatus.Should().Contain("release remains unconfirmed");
        f.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 0.5);
        f.ViewModel.RefreshSpeechPlaybackFrame();
        f.ViewModel.IsSpeechPlaybackActive.Should().BeTrue();

        var speaks = f.TextToSpeech.SpeakCalls;
        await f.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.SpeakCalls.Should().Be(speaks);
        await f.ViewModel.StopSpeakingFromTrayAsync();
        f.ViewModel.IsSpeaking.Should().BeFalse();
        f.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Be("Speech is stopped.");
        f.ViewModel.ResponseBody.Should().Be("No speech playback is active.");
    }

    [Theory]
    [InlineData("native", "active")]
    [InlineData("typed", "active")]
    [InlineData("voice", "active")]
    [InlineData("tray", "active")]
    [InlineData("native", "failure")]
    [InlineData("typed", "failure")]
    [InlineData("voice", "failure")]
    [InlineData("tray", "failure")]
    [InlineData("native", "cancelled")]
    [InlineData("typed", "cancelled")]
    [InlineData("voice", "cancelled")]
    [InlineData("tray", "cancelled")]
    public async Task OrdinarySpeechRecoveryNeverClaimsStopBeforePhysicalRelease(string route, string result)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (route is "voice")
        {
            await f.ViewModel.BeginPushToTalkAsync();
            await f.ViewModel.EndPushToTalkAsync();
        }
        f.TextToSpeech.ReturnWhileSpeaking = true;
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.RemainSpeakingOnStop = true;
        f.TextToSpeech.StopException = result switch
        {
            "failure" => new IOException("Synthetic output failure"),
            "cancelled" => new OperationCanceledException("Synthetic output cancellation"),
            _ => null,
        };
        var stops = f.TextToSpeech.StopCalls;
        var speaks = f.TextToSpeech.SpeakCalls;
        f.Reasoner.Requests.Clear();

        await StopOrdinarySpeechAsync(f, route);

        f.TextToSpeech.StopCalls.Should().Be(stops + 1);
        f.TextToSpeech.IsSpeaking.Should().BeTrue();
        f.ViewModel.IsSpeaking.Should().BeTrue();
        f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
        f.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.CanReadCurrentResponse.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Be("Speech output could not be stopped.");
        f.ViewModel.ResponseBody.Should().NotContain("No speech playback is active");
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpeakCalls.Should().Be(speaks);

        f.TextToSpeech.RemainSpeakingOnStop = false;
        f.TextToSpeech.StopException = null;
        await StopOrdinarySpeechAsync(f, route is "voice" ? "tray" : route);

        f.TextToSpeech.StopCalls.Should().Be(stops + 2);
        f.ViewModel.IsSpeaking.Should().BeFalse();
        f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Be("Speech is stopped.");
        f.ViewModel.ResponseBody.Should().Be("No speech playback is active.");
        f.TextToSpeech.SpeakCalls.Should().Be(speaks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinarySpeechRecoveryPreservesRunningOutputAfterStopFailure(bool cancel)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = f.RunAsync("what power action is pending");
        try
        {
            await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            f.TextToSpeech.StopException = cancel ? new OperationCanceledException("Synthetic cancellation")
                : new IOException("Synthetic failure");
            await f.ViewModel.StopSpeakingFromTrayAsync();
            speaking.IsCompleted.Should().BeFalse();
            f.TextToSpeech.IsSpeaking.Should().BeTrue();
            f.ViewModel.IsSpeaking.Should().BeTrue();
            f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
            f.ViewModel.ResponseTitle.Should().Be("Speech output could not be stopped.");
            f.TextToSpeech.StopException = null;
            await f.ViewModel.StopSpeakingFromTrayAsync();
            await speaking;
            f.ViewModel.IsSpeaking.Should().BeFalse();
            f.ViewModel.ResponseTitle.Should().Be("Speech is stopped.");
        }
        finally
        {
            f.TextToSpeech.SpeakGate.TrySetResult();
            await speaking;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinarySpeechRecoveryRetainsActiveStateAfterFailedSpeakCompletion(bool cancel)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = f.RunAsync("what power action is pending");
        try
        {
            await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            f.TextToSpeech.SpeakCompletionException = cancel ? new OperationCanceledException("Synthetic cancellation")
                : new IOException("Synthetic failure");
            f.TextToSpeech.SpeakGate.TrySetResult();
            await speaking;
            f.TextToSpeech.IsSpeaking.Should().BeTrue();
            f.ViewModel.IsSpeaking.Should().BeTrue();
            f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
            f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
            await f.ViewModel.StopSpeakingFromTrayAsync();
            f.ViewModel.IsSpeaking.Should().BeFalse();
        }
        finally
        {
            f.TextToSpeech.SpeakGate.TrySetResult();
            await speaking;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinarySpeechRecoveryWaitsForStopReturnAndChecksActualRelease(bool remainsActive)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ReturnWhileSpeaking = true;
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.RemainSpeakingOnStop = remainsActive;
        f.TextToSpeech.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.BeforeStop = () => accepted.TrySetResult();
        var stopping = f.ViewModel.StopSpeechCommand.ExecuteAsync();
        try
        {
            await accepted.Task.WaitAsync(TestContext.Current.CancellationToken);
            stopping.IsCompleted.Should().BeFalse();
            f.ViewModel.IsSpeaking.Should().BeTrue();
            f.ViewModel.ResponseTitle.Should().NotBe("Speech is stopped.");
        }
        finally
        {
            f.TextToSpeech.StopGate.TrySetResult();
            await stopping;
        }
        f.ViewModel.IsSpeaking.Should().Be(remainsActive);
        f.ViewModel.ResponseTitle.Should().Be(remainsActive ? "Speech output could not be stopped." : "Speech is stopped.");
        f.TextToSpeech.RemainSpeakingOnStop = false;
        await f.ViewModel.StopSpeakingFromTrayAsync();
    }

    [Fact]
    public async Task OrdinarySpeechRecoveryObservesLaterPhysicalReleaseWithoutReplay()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ReturnWhileSpeaking = true;
        await f.RunAsync("what power action is pending");
        f.ViewModel.IsSpeaking.Should().BeTrue();
        var speaks = f.TextToSpeech.SpeakCalls;
        await f.TextToSpeech.StopAsync(TestContext.Current.CancellationToken);
        f.ViewModel.RefreshSpeechPlaybackFrame();
        f.ViewModel.IsSpeaking.Should().BeFalse();
        f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeTrue();
        f.ViewModel.CanReadCurrentResponse.Should().BeTrue();
        f.TextToSpeech.SpeakCalls.Should().Be(speaks);
    }

    [Fact]
    public async Task OrdinarySpeechRecoveryRechecksProviderBeforePreviewStarts()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ReturnWhileSpeaking = true;
        await f.TextToSpeech.SpeakAsync("Synthetic existing playback", f.ViewModel.SelectedVoice!,
            f.ViewModel.SelectedOutputDevice!, TestContext.Current.CancellationToken);
        var speaks = f.TextToSpeech.SpeakCalls;
        await f.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        f.TextToSpeech.SpeakCalls.Should().Be(speaks);
        f.ViewModel.IsSpeaking.Should().BeTrue();
        f.ViewModel.ResponseOutputStatus.Should().Contain("release remains unconfirmed");
        await f.ViewModel.StopSpeakingFromTrayAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinarySpeechRecoveryDoesNotDispatchUnrelatedVoiceWorkBeforeRelease(bool remainsActive)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.TextToSpeech.ReturnWhileSpeaking = true;
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.RemainSpeakingOnStop = remainsActive;
        f.Reasoner.Requests.Clear();
        await f.Voice.RaiseTranscriptAsync("Kora, hide yourself", 1);
        f.ViewModel.IsSpeaking.Should().Be(remainsActive);
        if (remainsActive)
        {
            f.ViewModel.ResponseTitle.Should().Be("Speech output could not be stopped.");
            f.ViewModel.ResponseBody.Should().Contain("release remains unconfirmed");
            f.WindowActions.Should().NotContain(Kora.Application.ViewModels.WindowAction.Hide);
        }
        else { f.WindowActions.Should().Contain(Kora.Application.ViewModels.WindowAction.Hide); }
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.RemainSpeakingOnStop = false;
        await f.ViewModel.StopSpeakingFromTrayAsync();
    }

    [Fact]
    public async Task OrdinarySpeechRecoveryReviewedVoiceStopWithoutRetainedTargetUsesSharedStop()
    {
        var f = await ReviewedCommandFixture();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        await f.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.ViewModel.HasReviewedVoiceCommand.Should().BeTrue();
        var stops = f.TextToSpeech.StopCalls;
        await f.ViewModel.CaptureReviewedVoiceCommandConfirmation()();
        f.TextToSpeech.StopCalls.Should().Be(stops + 1);
        f.ViewModel.ResponseTitle.Should().Be("Speech is stopped.");
        f.ViewModel.ResponseBody.Should().Be("No speech playback is active.");
        f.ViewModel.IsSpeaking.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }
}
