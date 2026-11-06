using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("wrong-selection")]
    [InlineData("drift")]
    [InlineData("hold")]
    [InlineData("release")]
    public async Task ExactNameProfilePreparationStaysExclusiveAndCannotTransferQualification(string ending)
    {
        var stage = new Stage { SupportsNative = true };
        var native = new Activated();
        await using var voice = Create(native, stage);
        voice.InitializeProfile(SpeechInputSelection.Default);
        voice.GetProfiles("Kora").Should().ContainSingle();
        voice.GetProfiles("Nova").Should().HaveCount(2);
        voice.GetUnavailableReason(SpeechInputSelection.Default, "Nova").Should().BeNull();
        voice.GetUnavailableReason(SpeechInputProfileCatalogue.NativeWakeSelection, "Kora").Should().NotBeNull();
        var next = SpeechInputProfileCatalogue.NativeWakeSelection;
        var lease = await voice.RetireProfileAsync(next, "Nova", TestContext.Current.CancellationToken);
        voice.IsProfileAvailable.Should().BeFalse();
        stage.Opens.Should().Be(0);
        if (ending is "drift") { stage.SupportsNative = false; }
        if (ending is "wrong-selection" or "drift")
        {
            FluentActions.Invoking(() => lease.Publish(new(2, ending is "wrong-selection" ? SpeechInputSelection.Default : next)))
                .Should().Throw<InvalidOperationException>();
            lease.HoldUnavailable();
        }
        else if (ending is "hold") { lease.HoldUnavailable(); }
        else
        {
            FluentActions.Invoking(() => lease.Publish(new(5, next))).Should().Throw<InvalidOperationException>();
            lease.Publish(new(2, next));
            voice.ProfileGeneration.Selection.Should().Be(next);
            native.ProfileGeneration.Selection.Should().Be(SpeechInputSelection.Default, "the existing ASR is retained, not another wake engine");
        }
        if (ending is "release")
        {
            stage.Assets.FailRelease = true;
            await FluentActions.Awaiting(() => lease.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
            voice.IsProfileAvailable.Should().BeFalse();
            await FluentActions.Awaiting(() => voice.RetireProfileAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await lease.DisposeAsync();
            await lease.DisposeAsync();
            stage.Assets.Releases.Should().Be(1);
            voice.IsListening.Should().BeFalse();
            FluentActions.Invoking(() => lease.Publish(new(3, next))).Should().Throw<ObjectDisposedException>();
        }
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("prepare")]
    [InlineData("cancel")]
    public async Task MissingNameOrFailedPreparationHoldsInputAndDoesNotPublish(string failure)
    {
        var stage = new Stage { SupportsNative = failure is not "unsupported" };
        var native = new Activated();
        await using var voice = Create(native, stage);
        stage.PrepareFailure = failure switch
        {
            "prepare" => new InvalidOperationException("Fixture assets drifted."),
            "cancel" => new OperationCanceledException(),
            _ => null,
        };
        await FluentActions.Awaiting(() => voice.RetireProfileAsync(SpeechInputProfileCatalogue.NativeWakeSelection,
            "Nova", TestContext.Current.CancellationToken)).Should().ThrowAsync<Exception>();
        voice.ProfileGeneration.Number.Should().Be(1);
        voice.IsProfileAvailable.Should().BeFalse();
        stage.Opens.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownNativeStartupIntentDoesNotSelectUnsupportedResources(bool supportedTuple)
    {
        var stage = new Stage();
        await using var voice = Create(new Activated(), stage);
        var selection = SpeechInputProfileCatalogue.NativeWakeSelection;
        if (!supportedTuple) { selection = selection with { Resource = SpeechInputResource.Gpu }; }
        voice.InitializeProfile(selection);
        voice.ProfileGeneration.Selection.Should().Be(selection);
        voice.GetUnavailableReason(selection, "Nova").Should().NotBeNull();
        await using var lease = await voice.RetireProfileAsync(TestContext.Current.CancellationToken);
        if (!supportedTuple)
        {
            FluentActions.Invoking(() => lease.Publish(new(2, selection))).Should().Throw<InvalidOperationException>();
        }
        else { lease.Publish(new(2, SpeechInputSelection.Default)); }
        lease.HoldUnavailable();
    }

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
        var switching = voice.RetireProfileAsync(SpeechInputSelection.Default, "Kora", TestContext.Current.CancellationToken);
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
