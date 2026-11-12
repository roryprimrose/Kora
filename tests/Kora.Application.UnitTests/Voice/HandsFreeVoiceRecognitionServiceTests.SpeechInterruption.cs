using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Fact]
    public async Task WakeSpeechInterruptionRequiresIndependentQualificationWithoutOpeningOrdinaryWake()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var capture = await voice.StartSpeechInterruptionAsync(static () => true,
            (_, _) => throw new InvalidOperationException("Unqualified input must not dispatch."), TestContext.Current.CancellationToken);
        capture.Should().BeNull();
        stage.Opens.Should().Be(0);
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("no-admission")]
    [InlineData("ineligible")]
    [InlineData("changed-after-observation")]
    public async Task WakeSpeechInterruptionMissingOrChangingAdmissionNeverOpensCapture(string loss)
    {
        var stage = new Stage { InterruptionAvailable = true };
        await using var voice = Create(new(), stage);
        var observations = 0;
        if (loss is not "unbound")
        {
            voice.Bind(() => loss is "no-admission" || loss is "changed-after-observation" && ++observations > 1
                ? null : Admission, (_, _) => Task.CompletedTask, static () => true);
        }
        var start = () => voice.StartSpeechInterruptionAsync(() => loss is not "ineligible",
            (_, _) => throw new InvalidOperationException("Missing admission cannot dispatch."), TestContext.Current.CancellationToken);
        if (loss is "changed-after-observation") { await start.Should().ThrowAsync<OperationCanceledException>(); }
        else { (await start()).Should().BeNull(); }
        stage.Opens.Should().Be(0);
    }

    [Theory]
    [InlineData("interruption")]
    [InlineData("activated")]
    [InlineData("routing")]
    [InlineData("switching")]
    [InlineData("unconfirmed-profile")]
    public async Task WakeSpeechInterruptionCannotStealAnotherCaptureOrProfileOwner(string owner)
    {
        var stage = new Stage { InterruptionAvailable = true };
        await using var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        WakeSpeechInterruptionCapture? original = null;
        ISpeechInputProfileSwitch? switching = null;
        RoutingCaptureRelease? routing = null;
        if (owner is "interruption")
        {
            original = await voice.StartSpeechInterruptionAsync(static () => true, (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken);
            await stage.NextAsync();
        }
        if (owner is "activated")
        { await voice.BeginPushToTalkAsync(Admission.Microphone, [], cancellationToken: TestContext.Current.CancellationToken); }
        if (owner is "routing")
        {
            routing = new(voice.RoutingActivatedCapture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
            voice.AttachRoutingCapture(routing);
        }
        if (owner is "switching" or "unconfirmed-profile")
        {
            switching = await voice.RetireProfileAsync(TestContext.Current.CancellationToken);
            if (owner is "unconfirmed-profile") { switching.HoldUnavailable(); await switching.DisposeAsync(); switching = null; }
        }
        await FluentActions.Awaiting(() => voice.StartSpeechInterruptionAsync(static () => true,
            (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        if (original is not null) { await original.DisposeAsync(); }
        if (switching is not null) { await switching.DisposeAsync(); }
        if (routing is not null) { voice.DetachRoutingCapture(routing); }
        stage.Opens.Should().Be(owner is "interruption" ? 1 : 0);
    }

    [Fact]
    public async Task WakeSpeechInterruptionCommandDeadlineJoinsCaptureWithoutDispatch()
    {
        var stage = new Stage { InterruptionAvailable = true };
        var clock = new DeadlineTimeProvider();
        var source = new CaptureTimingSource { Timing = new(5, 15, 1, 10) };
        await using var voice = new HandsFreeVoiceRecognitionService(new Activated(), stage,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HandsFreeVoiceRecognitionService>.Instance, clock, source);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var capture = (await voice.StartSpeechInterruptionAsync(static () => true,
            (_, _) => throw new InvalidOperationException("Timed out input must not dispatch."), TestContext.Current.CancellationToken))!;
        var session = await stage.NextAsync();
        session.Wake.SetResult(new(session.Binding.Generation, 12, true));
        await session.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.DueTime.Should().Be(session.Binding.Timing.MaximumCapture);
        clock.Expire();
        await capture.CaptureClosed;
        await FluentActions.Awaiting(() => capture.DisposeAsync().AsTask()).Should().ThrowAsync<TimeoutException>();
        voice.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task WakeSpeechInterruptionCanBorrowClosedCaptureWhileTheOriginalWakeOperationIsStillCompleting()
    {
        var stage = new Stage { InterruptionAvailable = true };
        await using var voice = Create(new(), stage);
        var started = new TaskCompletionSource<WakeSpeechInterruptionCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        voice.Bind(() => Admission, async (_, _) =>
        {
            var capture = await voice.StartSpeechInterruptionAsync(static () => true, (_, _) =>
            {
                delivered.TrySetResult();
                return Task.CompletedTask;
            }, TestContext.Current.CancellationToken);
            started.TrySetResult(capture!);
            await releaseOperation.Task.WaitAsync(TestContext.Current.CancellationToken);
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var original = await stage.NextAsync();
        original.Wake.SetResult(new(original.Binding.Generation, 12, true));
        original.Result.SetResult(new("read selected answer", 1, original.Binding.Generation));
        var capture = await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var interrupting = await stage.NextAsync();
        original.IsQuiescent.Should().BeTrue();
        voice.Generation.Should().Be(original.Binding.Generation);
        interrupting.Wake.SetResult(new(interrupting.Binding.Generation, 12, true));
        interrupting.Result.SetResult(new("stop speaking", 1, interrupting.Binding.Generation));
        await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await capture.DisposeAsync();
        await voice.StopAsync(TestContext.Current.CancellationToken);
        releaseOperation.TrySetResult();
        await voice.Completion;
        stage.Opens.Should().Be(2);
    }

    [Fact]
    public async Task WakeSpeechInterruptionCallerCancellationPermanentlyRetiresAnOtherwiseCurrentPlayback()
    {
        var stage = new Stage { InterruptionAvailable = true };
        await using var voice = Create(new(), stage);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var capture = (await voice.StartSpeechInterruptionAsync(static () => true,
            (_, _) => throw new InvalidOperationException("Cancelled input cannot dispatch."), lifetime.Token))!;
        await stage.NextAsync();
        await lifetime.CancelAsync();
        await capture.CaptureClosed;
        capture.IsCurrent.Should().BeFalse();
        await capture.DisposeAsync();
        voice.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task WakeSpeechInterruptionJoinsCaptureBeforeDispatchAndKeepsOtherInputExcluded()
    {
        var stage = new Stage { InterruptionAvailable = true };
        var native = new Activated();
        await using var voice = Create(native, stage);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<bool>? currency = null;
        var capture = (await voice.StartSpeechInterruptionAsync(static () => true, async (text, current) =>
        {
            text.Transcript.Should().Be("stop speaking");
            currency = current;
            dispatched.TrySetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken))!;
        var session = await stage.NextAsync();
        session.HoldClose = true;
        session.Wake.SetResult(new(session.Binding.Generation, 12, true));
        session.Result.SetResult(new("stop speaking", 1, session.Binding.Generation));
        await session.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        voice.IsCaptureQuiescent.Should().BeFalse();
        voice.IsListening.Should().BeTrue();
        dispatched.Task.IsCompleted.Should().BeFalse();
        await FluentActions.Awaiting(() => voice.BeginPushToTalkAsync(Admission.Microphone, []))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.StartHandsFreeAsync()).Should().ThrowAsync<InvalidOperationException>();
        session.ReleaseClose.SetResult();
        await dispatched.Task.WaitAsync(TestContext.Current.CancellationToken);
        voice.IsCaptureQuiescent.Should().BeTrue();
        voice.IsListening.Should().BeFalse();
        currency!().Should().BeTrue();
        voice.Revalidate();
        currency().Should().BeTrue();
        stage.InterruptionAvailable = false;
        voice.Revalidate();
        stage.InterruptionAvailable = true;
        currency().Should().BeFalse("restoring qualification cannot revive a retired playback utterance");
        await FluentActions.Awaiting(() => voice.BeginReplyAsync(Admission.Microphone, [], static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        release.SetResult();
        await capture.DisposeAsync();
        currency().Should().BeFalse();
        native.PushToTalkCalls.Should().Be(0);
        stage.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData("playback")]
    [InlineData("admission")]
    [InlineData("qualification")]
    [InlineData("wake")]
    [InlineData("wake-generation")]
    [InlineData("boundary")]
    [InlineData("generation")]
    [InlineData("transcript")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("evidence")]
    [InlineData("low-confidence")]
    [InlineData("high-confidence")]
    public async Task WakeSpeechInterruptionRejectsRetiredOrUntrustedObservationsAfterJoining(string loss)
    {
        var stage = new Stage { InterruptionAvailable = true };
        await using var voice = Create(new(), stage);
        HandsFreeVoiceAdmission? observation = Admission;
        var eligible = true;
        var dispatches = 0;
        voice.Bind(() => observation, (_, _) => Task.CompletedTask, static () => true);
        var capture = (await voice.StartSpeechInterruptionAsync(() => eligible, (_, _) =>
        {
            dispatches++;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken))!;
        var session = await stage.NextAsync();
        session.HoldClose = true;
        var invalidWake = loss is "wake" or "wake-generation" or "boundary";
        session.Wake.SetResult(new(session.Binding.Generation + (loss is "wake-generation" ? 1 : 0),
            loss is "boundary" ? -1 : 12, loss is not "wake"));
        if (!invalidWake)
        {
            await session.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            var text = loss switch { "empty" => " ", "oversized" => new string('x', VoiceInputLimits.MaximumTranscriptCharacters + 1), _ => "stop speaking" };
            var confidence = loss switch { "transcript" => float.NaN, "low-confidence" => 0.1f, "high-confidence" => 1.1f, _ => 1 };
            session.Result.SetResult(new(text, confidence,
                session.Binding.Generation + (loss is "generation" ? 1 : 0),
                loss is "evidence" ? new ProviderEvidence(session.Binding.Generation) : null));
        }
        await session.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (loss is "playback") { eligible = false; }
        if (loss is "admission") { observation = Admission with { OwnershipBindingRevision = 2 }; }
        if (loss is "qualification") { stage.InterruptionAvailable = false; }
        session.ReleaseClose.SetResult();
        await capture.CaptureClosed;
        if (loss is not ("playback" or "admission" or "qualification"))
        { await FluentActions.Awaiting(() => capture.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>(); }
        else { await capture.DisposeAsync(); }
        dispatches.Should().Be(0);
        session.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task WakeSpeechInterruptionStopWaitsForIgnoredNativeOpenAndPhysicalJoin()
    {
        var stage = new Stage { InterruptionAvailable = true, HoldOpen = true };
        await using var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var capture = (await voice.StartSpeechInterruptionAsync(static () => true,
            (_, _) => throw new InvalidOperationException("Retired input cannot dispatch."), TestContext.Current.CancellationToken))!;
        var session = await stage.NextAsync();
        session.HoldClose = true;
        var stop = voice.StopAsync(TestContext.Current.CancellationToken);
        stop.IsCompleted.Should().BeFalse();
        voice.IsCaptureQuiescent.Should().BeFalse();
        stage.ReleaseOpen.SetResult();
        await session.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        stop.IsCompleted.Should().BeFalse();
        session.ReleaseClose.SetResult();
        await stop;
        await capture.DisposeAsync();
        session.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WakeSpeechInterruptionFailedReleaseRetainsOwnershipAndNeverDispatches(bool throws)
    {
        var stage = new Stage { InterruptionAvailable = true };
        var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var capture = (await voice.StartSpeechInterruptionAsync(static () => true,
            (_, _) => throw new InvalidOperationException("Unjoined input cannot dispatch."), TestContext.Current.CancellationToken))!;
        var session = await stage.NextAsync();
        session.FailClose = throws;
        session.ConfirmQuiescence = false;
        session.Wake.SetResult(new(session.Binding.Generation, 12, true));
        session.Result.SetResult(new("stop speaking", 1, session.Binding.Generation));
        await session.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => capture.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
        voice.IsCaptureQuiescent.Should().BeFalse();
        await FluentActions.Awaiting(() => voice.StartHandsFreeAsync()).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
        stage.Opens.Should().Be(1);
    }
}
