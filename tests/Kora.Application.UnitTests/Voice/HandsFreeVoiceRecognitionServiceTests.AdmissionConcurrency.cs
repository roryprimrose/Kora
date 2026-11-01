using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Theory]
    [InlineData("can-open")]
    [InlineData("activation")]
    [InlineData("changed")]
    public async Task ExternalCaptureAndActivationCallbacksDoNotOwnTheVoiceMonitor(string callback)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var armed = 0;
        void Pause()
        {
            if (Interlocked.Exchange(ref armed, 0) != 1) { return; }
            entered.TrySetResult();
            release.Wait(TestContext.Current.CancellationToken);
        }
        voice.Bind(() => Admission, static (_, _) => Task.CompletedTask,
            () => { if (callback is "can-open") { Pause(); } return true; },
            _ => { if (callback is "activation") { Pause(); } });
        void Changed(object? sender, EventArgs args)
        {
            if (callback is "changed" && voice.State == HandsFreeVoiceState.CapturingCommand) { Pause(); }
        }
        voice.Changed += Changed;
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        Volatile.Write(ref armed, 1);
        var notifying = Task.Run(() => opened.Binding.WakeActivated(new(opened.Binding.Generation, 3, true)),
            TestContext.Current.CancellationToken);
        Task<bool>? status = null;
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            status = Task.Run(() => voice.IsProfileAvailable, TestContext.Current.CancellationToken);
            (await status.WaitAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            notifying.IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.Set();
            try { await Task.WhenAll(notifying, status is null ? Task.CompletedTask : status); }
            finally { voice.Changed -= Changed; }
        }
        voice.State.Should().Be(HandsFreeVoiceState.CapturingCommand);
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        opened.IsQuiescent.Should().BeTrue();
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task ActivationCanProgressDuringAnOlderObservationAndThatIdleSnapshotCannotAuthorizeCommandPcm()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var observation = Admission;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var observationArmed = 0;
        var activations = 0;
        voice.Bind(() =>
        {
            var current = Volatile.Read(ref observation);
            if (Interlocked.Exchange(ref observationArmed, 0) == 1)
            {
                entered.TrySetResult();
                release.Wait(TestContext.Current.CancellationToken);
            }
            return current;
        }, static (_, _) => Task.CompletedTask, static () => true, _ => Interlocked.Increment(ref activations));
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        Volatile.Write(ref observationArmed, 1);
        var checking = Task.Run(opened.Binding.CanCapture, TestContext.Current.CancellationToken);
        Task? activating = null;
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Volatile.Write(ref observation, Admission with { ComposerRevision = 2 });
            var wake = new WakeWordObservation(opened.Binding.Generation, 3, true);
            activating = Task.Run(() => opened.Binding.WakeActivated(wake), TestContext.Current.CancellationToken);
            await activating.WaitAsync(TestContext.Current.CancellationToken);
            voice.State.Should().Be(HandsFreeVoiceState.CapturingCommand);
            activations.Should().Be(1);
        }
        finally
        {
            release.Set();
            await Task.WhenAll(checking, activating ?? Task.CompletedTask);
        }
        (await checking).Should().BeFalse("the old idle phase cannot ignore composer currency after activation");
        opened.Binding.CanCapture().Should().BeTrue();
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        opened.IsQuiescent.Should().BeTrue();
        opened.CaptureCalls.Should().Be(0);
        stage.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData("pcm")]
    [InlineData("activation")]
    [InlineData("revalidate")]
    public async Task InternalRetirementDuringObservationCannotReturnCurrentOrActivate(string operation)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        Action? duringObservation = null;
        var activations = 0;
        var dispatches = 0;
        voice.Bind(() =>
        {
            var current = Admission;
            Interlocked.Exchange(ref duringObservation, null)?.Invoke();
            return current;
        }, (_, _) => { dispatches++; return Task.CompletedTask; }, static () => true, _ => activations++);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        duringObservation = voice.InvalidateCapture;
        if (operation is "pcm") { opened.Binding.CanCapture().Should().BeFalse(); }
        else if (operation is "activation")
        {
            FluentActions.Invoking(() => opened.Binding.WakeActivated(new(opened.Binding.Generation, 3, true)))
                .Should().Throw<OperationCanceledException>();
        }
        else { voice.Revalidate(); }
        await voice.Completion;
        opened.Binding.CanCapture().Should().BeFalse();
        opened.IsQuiescent.Should().BeTrue();
        activations.Should().Be(0);
        dispatches.Should().Be(0);
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task CaptureOwnerObservationCannotHideAPostActivationComposerChange()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var observation = Admission;
        Action? duringCanOpen = null;
        voice.Bind(() => observation, static (_, _) => Task.CompletedTask, () =>
        {
            Interlocked.Exchange(ref duringCanOpen, null)?.Invoke();
            return true;
        });
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        var wake = new WakeWordObservation(opened.Binding.Generation, 3, true);
        opened.Binding.WakeActivated(wake);
        duringCanOpen = () => observation = Admission with { ComposerRevision = 2 };
        opened.Binding.CanCapture().Should().BeFalse();
        FluentActions.Invoking(() => opened.Binding.WakeActivated(wake)).Should().Throw<InvalidOperationException>();
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        opened.IsQuiescent.Should().BeTrue();
        stage.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvidenceObservationCannotConsumeOrReviveAnInternallyRetiredUtterance(bool consumeFirst)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        Action? duringObservation = null;
        var delivered = new TaskCompletionSource<(VoiceTranscriptEventArgs Utterance, HostRequest Request)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        voice.Bind(() =>
        {
            Interlocked.Exchange(ref duringObservation, null)?.Invoke();
            return Admission;
        }, async (utterance, request) =>
        {
            delivered.TrySetResult((utterance, request));
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.TrySetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.TrySetResult(new("compare these plans", 1, opened.Binding.Generation));
        try
        {
            var original = await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Func<bool>? current = consumeFirst ? voice.ConsumeWakeActivation(original.Utterance, original.Request) : null;
            duringObservation = voice.InvalidateCapture;
            if (current is not null) { current().Should().BeFalse(); current().Should().BeFalse(); }
            else
            {
                FluentActions.Invoking(() => voice.ConsumeWakeActivation(original.Utterance, original.Request))
                    .Should().Throw<InvalidOperationException>();
            }
            FluentActions.Invoking(() => voice.ConsumeWakeActivation(original.Utterance, original.Request))
                .Should().Throw<InvalidOperationException>();
            voice.IsCaptureQuiescent.Should().BeTrue();
        }
        finally { release.TrySetResult(); }
        await voice.Completion;
        opened.IsQuiescent.Should().BeTrue();
        stage.Opens.Should().Be(1);
    }
}
