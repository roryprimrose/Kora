using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListenerComposerCurrencyIsCapturedOnceAtWakeAndNotAtIdleOpen(bool editAfterActivation)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var observation = Admission;
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        voice.Bind(() => observation, async (utterance, request) =>
        {
            voice.ConsumeWakeActivation(utterance, request)().Should().BeTrue();
            delivered.TrySetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        observation = Admission with { ComposerRevision = 2 };
        voice.Revalidate();
        opened.Invalidated.Should().BeFalse();
        voice.State.Should().Be(HandsFreeVoiceState.WakeListening);
        opened.Binding.CanCapture().Should().BeTrue();
        var wake = new WakeWordObservation(opened.Binding.Generation, 3, true);
        opened.Binding.WakeActivated(wake);
        voice.State.Should().Be(HandsFreeVoiceState.CapturingCommand);
        if (editAfterActivation) { observation = observation with { ComposerRevision = 3 }; }
        opened.Wake.SetResult(wake);
        opened.Result.TrySetResult(new("compare these plans", 1, opened.Binding.Generation));
        try
        {
            if (editAfterActivation)
            {
                await voice.Completion;
                delivered.Task.IsCompleted.Should().BeFalse();
                voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
            }
            else
            {
                await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
                await voice.StopAsync(TestContext.Current.CancellationToken);
            }
        }
        finally { release.TrySetResult(); }
        await voice.Completion;
        opened.IsQuiescent.Should().BeTrue();
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task JoinedHostUtteranceRejectsCopiesOtherProducersRequestsAndReplay()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        await using var other = Create(new(), new());
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        VoiceTranscriptEventArgs? original = null;
        HostRequest? originalRequest = null;
        Func<bool>? current = null;
        voice.Bind(() => Admission with { AssistantName = "Nova" }, async (utterance, request) =>
        {
            original = utterance;
            originalRequest = request;
            utterance.Transcript.Should().Be("compare these plans");
            utterance.ActivationEvidence.Should().NotBeNull();
            utterance.ActivationEvidence!.Generation.Should().Be(utterance.Generation);
            voice.IsCaptureQuiescent.Should().BeTrue();
            var copy = new VoiceTranscriptEventArgs(utterance.Transcript, utterance.Confidence,
                utterance.Generation, utterance.ActivationEvidence);
            FluentActions.Invoking(() => voice.ConsumeWakeActivation(
                new(utterance.Transcript, utterance.Confidence, utterance.Generation), request))
                .Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => voice.ConsumeWakeActivation(copy, request))
                .Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => voice.ConsumeWakeActivation(utterance, request with { }))
                .Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => voice.ConsumeWakeActivation(utterance,
                new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.LocalUi)))
                .Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => other.ConsumeWakeActivation(utterance, request))
                .Should().Throw<InvalidOperationException>();
            current = voice.ConsumeWakeActivation(utterance, request);
            current().Should().BeTrue();
            FluentActions.Invoking(() => voice.ConsumeWakeActivation(utterance, request))
                .Should().Throw<InvalidOperationException>();
            using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(utterance));
            serialized.RootElement.TryGetProperty(nameof(VoiceTranscriptEventArgs.ActivationEvidence), out _)
                .Should().BeFalse();
            delivered.SetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.HoldClose = true;
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("compare these plans", 1, opened.Binding.Generation));
        await opened.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        delivered.Task.IsCompleted.Should().BeFalse();
        voice.IsCaptureQuiescent.Should().BeFalse();
        opened.ReleaseClose.SetResult();
        await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await voice.StopAsync(TestContext.Current.CancellationToken);
        current!().Should().BeFalse();
        release.SetResult();
        await voice.Completion;
        FluentActions.Invoking(() => voice.ConsumeWakeActivation(original!, originalRequest!))
            .Should().Throw<InvalidOperationException>();
        stage.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("name-revision")]
    [InlineData("profile")]
    [InlineData("microphone")]
    [InlineData("topology")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("control-binding")]
    [InlineData("consent")]
    [InlineData("privacy")]
    [InlineData("input")]
    [InlineData("session")]
    public async Task ConsumedWakeRemainsBoundToItsOriginalAdmissionAcrossAwaitedConsumerWork(string change)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        HandsFreeVoiceAdmission? observation = Admission;
        var delivered = new TaskCompletionSource<Func<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        voice.Bind(() => observation, async (utterance, request) =>
        {
            delivered.SetResult(voice.ConsumeWakeActivation(utterance, request));
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("compare these plans", 1, opened.Binding.Generation));
        var current = await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
        current().Should().BeTrue();
        observation = change switch
        {
            "name" => Admission with { AssistantName = "Nova" },
            "profile" => Admission with { Profile = Admission.Profile with { Number = 2 } },
            "microphone" => Admission with { Microphone = new("other", "Other") },
            "topology" => Admission with { TopologyRevision = 2 },
            "call" => Admission with { CallRevision = 2 },
            "owner" => Admission with { OwnerRevision = 2 },
            "binding" => Admission with { OwnershipBindingRevision = 2 },
            "name-revision" => Admission with { NameRevision = 2 },
            "control-binding" => Admission with { ControlBindingRevision = 2 },
            "consent" => Admission with { RecoveryRevision = 2 },
            "input" => Admission with { ComposerRevision = 2 },
            "session" => Admission with { ConversationRevision = 2 },
            _ => null,
        };
        current().Should().BeFalse();
        observation = Admission;
        current().Should().BeFalse("revoked evidence cannot revive even if an observation is restored");
        voice.InvalidateCapture();
        release.SetResult();
        await voice.Completion;
        current().Should().BeFalse();
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task ProfileRetirementRevokesConsumedEvidenceBeforePublishingAReplacementProfile()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var delivered = new TaskCompletionSource<Func<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        voice.Bind(() => Admission, async (utterance, request) =>
        {
            delivered.SetResult(voice.ConsumeWakeActivation(utterance, request));
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("compare these plans", 1, opened.Binding.Generation));
        var current = await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
        current().Should().BeTrue();
        await using (var switching = await voice.RetireProfileAsync(TestContext.Current.CancellationToken))
        {
            current().Should().BeFalse();
            switching.Publish(new(2, SpeechInputSelection.Default));
        }
        current().Should().BeFalse();
        release.SetResult();
        await voice.Completion;
        opened.IsQuiescent.Should().BeTrue();
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task ProviderEvidenceCannotBePromotedIntoHostWakeEvidence()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var calls = 0;
        voice.Bind(() => Admission, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("compare these plans", 1, opened.Binding.Generation,
            new ProviderEvidence(opened.Binding.Generation)));
        await voice.Completion;
        calls.Should().Be(0);
        opened.IsQuiescent.Should().BeTrue();
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task PreviousNativeActivationCallbackCannotReplaceAFreshListeningGeneration()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var calls = 0;
        voice.Bind(() => Admission, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 3, true));
        first.Result.SetResult(null);
        var second = await stage.NextAsync();
        second.Binding.Generation.Should().NotBe(first.Binding.Generation);
        FluentActions.Invoking(() => first.Binding.WakeActivated(new(first.Binding.Generation, 3, true)))
            .Should().Throw<InvalidOperationException>();
        voice.State.Should().Be(HandsFreeVoiceState.WakeListening);
        second.Binding.CanCapture().Should().BeTrue();
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        first.IsQuiescent.Should().BeTrue();
        second.IsQuiescent.Should().BeTrue();
        second.CaptureCalls.Should().Be(0);
        calls.Should().Be(0);
        stage.Opens.Should().Be(2);
    }

    [Fact]
    public async Task LateNativeActivationAfterPolicyRetirementCannotReopenACompletedWorker()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var observation = Admission;
        var calls = 0;
        voice.Bind(() => observation, (utterance, request) =>
        {
            voice.ConsumeWakeActivation(utterance, request)().Should().BeTrue();
            calls++;
            observation = Admission with { AssistantName = "Nova" };
            return Task.CompletedTask;
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("compare these plans", 1, opened.Binding.Generation));
        await voice.Completion;
        voice.State.Should().Be(HandsFreeVoiceState.Stopped);
        opened.IsQuiescent.Should().BeTrue();
        FluentActions.Invoking(() => opened.Binding.WakeActivated(new(opened.Binding.Generation, 3, true)))
            .Should().Throw<InvalidOperationException>();
        opened.Binding.CanCapture().Should().BeFalse();
        calls.Should().Be(1);
        stage.Opens.Should().Be(1);
    }

    private sealed class ProviderEvidence(long generation) : IVoiceActivationEvidence
    {
        public long Generation { get; } = generation;
    }

    [Fact]
    public async Task NativeActivationDuringOpenPublishesCommandPhaseAndRejectsAReplacementSpan()
    {
        var stage = new Stage
        {
            OnOpened = opened => opened.Binding.WakeActivated(new(opened.Binding.Generation, 3, true)),
        };
        await using var voice = Create(new(), stage);
        var calls = 0;
        voice.Bind(() => Admission, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        voice.State.Should().Be(HandsFreeVoiceState.CapturingCommand);
        FluentActions.Invoking(() => opened.Binding.WakeActivated(new(opened.Binding.Generation, 4, true)))
            .Should().Throw<InvalidOperationException>();
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        opened.IsQuiescent.Should().BeTrue();
        opened.CaptureCalls.Should().Be(0);
        calls.Should().Be(0);
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task HostChangeAfterTheFirstJoinedObservationCannotMintWakeEvidence()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var observation = Admission;
        Action? afterObservation = null;
        var calls = 0;
        voice.Bind(() =>
        {
            var snapshot = observation;
            Interlocked.Exchange(ref afterObservation, null)?.Invoke();
            return snapshot;
        }, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.OnClosed = () => afterObservation = () => observation = Admission with { OwnerRevision = 2 };
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("compare these plans", 1, opened.Binding.Generation));
        await voice.Completion;
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
        opened.IsQuiescent.Should().BeTrue();
        calls.Should().Be(0);
        stage.Opens.Should().Be(1);
    }
}
