using AwesomeAssertions;

using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Fact]
    public async Task ComparisonReservesEveryInputRouteWithoutStoppingOrPublishingProfileAndReleaseIsIdempotent()
    {
        var native = new ComparisonActivated();
        await using var voice = Create(native, new Stage());
        voice.GetComparisonProfiles().Should().Equal(SpeechSetupComparisonTests.Profile);
        var generation = voice.ProfileGeneration;
        var lease = await voice.AcquireComparisonAsync(generation, Admission.Microphone, static () => true,
            TestContext.Current.CancellationToken);
        voice.IsCaptureQuiescent.Should().BeFalse();
        await FluentActions.Awaiting(() => voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.BeginPushToTalkAsync(Admission.Microphone, [], cancellationToken: TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.BeginReplyAsync(Admission.Microphone, [], static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.AcquireComparisonAsync(generation, Admission.Microphone, static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.RetireProfileAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.RetireProfileAsync(SpeechInputProfileCatalogue.NativeWakeSelection,
            "Nova", TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        FluentActions.Invoking(() => voice.InitializeProfile(SpeechInputSelection.Default)).Should().Throw<InvalidOperationException>();
        await voice.ResumeAfterActivatedInputAsync();
        var observation = await lease.CaptureAsync(SpeechSetupComparisonTests.Profile, TestContext.Current.CancellationToken);
        observation.FinalText.Should().Be("inert");
        native.PushToTalkCalls.Should().Be(0);
        native.ProfileRetireCalls.Should().Be(0);
        var releasing = lease.DisposeAsync().AsTask();
        releasing.IsCompleted.Should().BeFalse();
        voice.IsCaptureQuiescent.Should().BeFalse();
        native.Release.SetResult();
        await releasing;
        await lease.DisposeAsync();
        voice.IsCaptureQuiescent.Should().BeTrue();
        voice.ProfileGeneration.Should().Be(generation);
        voice.IsListening.Should().BeFalse();
        await FluentActions.Awaiting(() => lease.CaptureAsync(SpeechSetupComparisonTests.Profile, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ObjectDisposedException>();
        await using var next = await voice.AcquireComparisonAsync(generation, Admission.Microphone, static () => true,
            TestContext.Current.CancellationToken);
        await lease.DisposeAsync();
        voice.IsCaptureQuiescent.Should().BeFalse();
    }

    [Fact]
    public async Task ProfilePreparationAndUnconfirmedAssetReleaseCannotAcquireComparison()
    {
        var stage = new Stage { SupportsNative = true };
        var native = new ComparisonActivated();
        await using var voice = Create(native, stage);
        var original = voice.ProfileGeneration;
        var switching = await voice.RetireProfileAsync(SpeechInputProfileCatalogue.NativeWakeSelection,
            "Nova", TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => voice.AcquireComparisonAsync(original, Admission.Microphone,
            static () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        switching.Publish(new(2, SpeechInputProfileCatalogue.NativeWakeSelection));
        stage.Assets.FailRelease = true;
        await FluentActions.Awaiting(() => switching.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
        voice.ProfileGeneration.Should().Be(new SpeechInputGeneration(2, SpeechInputProfileCatalogue.NativeWakeSelection));
        native.ProfileGeneration.Selection.Should().Be(SpeechInputSelection.Default);
        voice.IsProfileAvailable.Should().BeFalse();
        await FluentActions.Awaiting(() => voice.AcquireComparisonAsync(voice.ProfileGeneration, Admission.Microphone,
            static () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        native.ComparisonAcquires.Should().Be(0);
        native.PushToTalkCalls.Should().Be(0);
        stage.Opens.Should().Be(0);
    }

    [Fact]
    public async Task MissingDeniedBusyAndFailedNativeAcquisitionCannotInventComparisonReadiness()
    {
        await using var missing = Create(new Activated(), new Stage());
        missing.GetComparisonProfiles().Should().BeEmpty();
        await FluentActions.Awaiting(() => missing.AcquireComparisonAsync(missing.ProfileGeneration, Admission.Microphone,
            static () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        var native = new ComparisonActivated { FailAcquire = true };
        await using var voice = Create(native, new Stage());
        await FluentActions.Awaiting(() => voice.AcquireComparisonAsync(voice.ProfileGeneration, Admission.Microphone,
            static () => false, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => voice.AcquireComparisonAsync(voice.ProfileGeneration, Admission.Microphone,
            static () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<IOException>();
        voice.IsCaptureQuiescent.Should().BeTrue();
        await voice.BeginPushToTalkAsync(Admission.Microphone, [], cancellationToken: TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => voice.AcquireComparisonAsync(voice.ProfileGeneration, Admission.Microphone,
            static () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ComparisonActivated : Activated, ISpeechSetupComparisonRuntime
    {
        public bool FailAcquire { get; set; }
        public int ComparisonAcquires { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() => [SpeechSetupComparisonTests.Profile];
        public Task<ISpeechSetupComparisonLease> AcquireComparisonAsync(SpeechInputGeneration generation,
            MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken)
        {
            ComparisonAcquires++;
            if (FailAcquire) { throw new IOException("Synthetic acquisition failure"); }
            return Task.FromResult<ISpeechSetupComparisonLease>(new Lease(this));
        }
        private sealed class Lease(ComparisonActivated owner) : ISpeechSetupComparisonLease
        {
            public Task<SpeechComparisonObservation> CaptureAsync(SpeechComparisonProfile profile, Action recordingStarted,
                CancellationToken cancellationToken)
            {
                recordingStarted();
                return Task.FromResult(new SpeechComparisonObservation("inert", TimeSpan.Zero));
            }
            public ValueTask DisposeAsync() => new(owner.Release.Task);
        }
    }
}
