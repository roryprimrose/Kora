using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;

using Neovolve.Logging.Xunit;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection("Host tracing")]
public sealed class WindowsActivatedCaptureResidencyTests(ITestOutputHelper output)
    : LoggingTestsBase<WindowsVoiceRecognitionService>(output)
{
    private static readonly MicrophoneDevice Microphone = new("mic", "Fixture microphone");
    private static readonly SpeechInputSelection KeepLoaded = SpeechInputSelection.Default with { Residency = SpeechInputResidency.KeepLoaded };

    [Theory]
    [InlineData(SpeechInputResidency.KeepLoaded, 1)]
    [InlineData(SpeechInputResidency.CaptureLifetime, 2)]
    public async Task SerialCapturesReuseOnlyTheModelAndReleaseDistinctMicrophoneAndStreamLifetimes(
        SpeechInputResidency residency, int expectedEngines)
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        var profile = new SpeechInputGeneration(7, SpeechInputSelection.Default with { Residency = residency });
        native.Engines.Should().BeEmpty();
        for (var index = 0; index < 2; index++)
        {
            using var stream = new BlockingAudioStream();
            var capture = await Open(factory, stream, profile);
            capture.Start();
            native.Microphones[^1].Audio([1, 2, 3]);
            stream.BufferedBytes.Should().Be(0, "audio has no routing subscriber in this fixture");
            stream.Add([4, 5, 6]);
            capture.FinishRecognition(cancel: false);
            await capture.DisposeAsync();
            stream.BufferedBytes.Should().Be(0);
            stream.CanRead.Should().BeFalse();
            native.Engines[^1].Stream.Should().BeNull();
            native.Engines[^1].SubscriberCount.Should().Be(0);
            native.Microphones.Should().OnlyContain(microphone => microphone.Released.Task.IsCompletedSuccessfully);
            factory.IsQuiescent.Should().BeTrue();
        }
        native.Engines.Should().HaveCount(expectedEngines);
        native.Microphones.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        native.Engines.Sum(engine => engine.DisposeCount).Should().Be(residency == SpeechInputResidency.KeepLoaded ? 0 : 2);
        await factory.RetireAsync();
        native.Engines.Should().OnlyContain(engine => engine.DisposeCount == 1);
    }

    [Theory]
    [InlineData("grammar")]
    [InlineData("generation")]
    [InlineData("timing")]
    [InlineData("residency")]
    public async Task ChangedImmutableKeyJoinsOldModelBeforeCreatingAReplacement(string change)
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        var profile = new SpeechInputGeneration(1, KeepLoaded);
        using var firstStream = new BlockingAudioStream();
        string[] phrases = ["help"];
        var first = await factory.OpenAsync(Microphone, phrases, firstStream, () => true, TimeSpan.FromSeconds(5), profile);
        first.Start();
        first.FinishRecognition(false);
        await first.DisposeAsync();
        var old = native.Engines.Single();
        old.DisposeGate = Signal();
        using var nextStream = new BlockingAudioStream();
        var next = factory.OpenAsync(Microphone, change is "grammar" ? ["stop"] : phrases, nextStream, () => true,
            TimeSpan.FromSeconds(change is "timing" ? 15 : 5),
            change is "generation" ? profile with { Number = 2 }
                : change is "residency" ? profile with { Selection = SpeechInputSelection.Default } : profile);
        await old.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        native.Engines.Should().ContainSingle();
        native.Microphones.Should().ContainSingle();
        factory.IsQuiescent.Should().BeFalse();
        old.DisposeGate.SetResult();
        var capture = await next;
        capture.Start();
        capture.FinishRecognition(false);
        await capture.DisposeAsync();
        native.Engines.Should().HaveCount(2);
        await factory.RetireAsync();
    }

    [Fact]
    public async Task GrammarSnapshotCannotBeMutatedByTheCaller()
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        string[] phrases = ["help"];
        using var stream = new BlockingAudioStream();
        var first = await factory.OpenAsync(Microphone, phrases, stream, () => true, TimeSpan.FromSeconds(5), new(1, KeepLoaded));
        phrases[0] = "changed";
        first.Start();
        first.FinishRecognition(false);
        await first.DisposeAsync();
        native.Engines.Single().Phrases.Should().Equal("help");
        using var secondStream = new BlockingAudioStream();
        var second = await Open(factory, secondStream);
        second.Start();
        second.FinishRecognition(false);
        await second.DisposeAsync();
        native.Engines.Should().ContainSingle();
        await factory.RetireAsync();
    }

    [Fact]
    public async Task RetirementDuringChangedGrammarDisposalCannotCreateTheReplacementModel()
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        using var stream = new BlockingAudioStream();
        var first = await Open(factory, stream);
        first.Start();
        first.FinishRecognition(false);
        await first.DisposeAsync();
        var engine = native.Engines.Single();
        engine.DisposeGate = Signal();
        var admitted = true;
        using var nextStream = new BlockingAudioStream();
        var opening = factory.OpenAsync(Microphone, ["changed"], nextStream, () => admitted,
            TimeSpan.FromSeconds(5), new(1, KeepLoaded));
        await engine.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        admitted = false;
        var retiring = factory.RetireAsync();
        engine.DisposeGate.SetResult();
        await FluentActions.Awaiting(() => opening).Should().ThrowAsync<OperationCanceledException>();
        await retiring;
        native.Engines.Should().ContainSingle();
        native.Microphones.Should().ContainSingle();
        factory.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task InFlightNativeCallbackMustReturnBeforeModelRetirementCanJoin()
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        using var stream = new BlockingAudioStream();
        var capture = await Open(factory, stream);
        capture.Start();
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        capture.TranscriptRecognized += (_, _) =>
        {
            entered.TrySetResult();
#pragma warning disable VSTHRD002 // Deliberately hold a synchronous native callback on a pool thread until the test releases it.
            release.Wait(TestContext.Current.CancellationToken);
#pragma warning restore VSTHRD002
        };
        var callback = Task.Run(native.Engines.Single().QueueTranscript("PRIVATE_IN_FLIGHT"), CancellationToken.None);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var retiring = factory.RetireAsync();
        var disposing = capture.DisposeAsync().AsTask();
        try
        {
            await native.Microphones.Single().Released.Task.WaitAsync(TestContext.Current.CancellationToken);
            retiring.IsCompleted.Should().BeFalse();
            disposing.IsCompleted.Should().BeFalse();
            factory.IsQuiescent.Should().BeFalse();
            native.Engines.Single().DisposeCount.Should().Be(0);
            using var deniedStream = new BlockingAudioStream();
            await FluentActions.Awaiting(() => Open(factory, deniedStream)).Should().ThrowAsync<InvalidOperationException>();
        }
        finally { release.Set(); }
        await callback;
        await disposing;
        await retiring;
        native.Engines.Single().DisposeCount.Should().Be(1);
        factory.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task ResetAndCallbackRetirementMustJoinBeforeReuseAndLateCallbacksCannotDispatch()
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        using var stream = new BlockingAudioStream();
        var first = await Open(factory, stream);
        first.Start();
        var engine = native.Engines.Single();
        var late = engine.QueueTranscript("PRIVATE_OLD");
        var results = new List<string>();
        first.TranscriptRecognized += (_, args) => results.Add(args.Transcript);
        stream.Add([1, 2, 3]);
        engine.ResetGate = Signal();
        first.FinishRecognition(false);
        var disposing = first.DisposeAsync().AsTask();
        await engine.ResetEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        native.Microphones.Single().Released.Task.IsCompletedSuccessfully.Should().BeTrue();
        stream.BufferedBytes.Should().Be(0);
        using var deniedStream = new BlockingAudioStream();
        await FluentActions.Awaiting(() => Open(factory, deniedStream)).Should().ThrowAsync<InvalidOperationException>();
        factory.IsQuiescent.Should().BeFalse();
        engine.ResetGate.SetResult();
        await disposing;
        using var nextStream = new BlockingAudioStream();
        var second = await Open(factory, nextStream);
        second.TranscriptRecognized += (_, args) => results.Add(args.Transcript);
        second.Start();
        late();
        results.Should().BeEmpty();
        second.FinishRecognition(false);
        await second.DisposeAsync();
        native.Engines.Should().ContainSingle();
        await factory.RetireAsync();
    }

    [Fact]
    public async Task UncooperativeCompletionRetainsOwnershipAndRetirementJoinsInsteadOfReplacing()
    {
        var native = new Native { CompleteOnFinish = false };
        var factory = new WindowsActivatedCaptureFactory(native);
        using var stream = new BlockingAudioStream();
        var capture = await Open(factory, stream);
        capture.Start();
        stream.Add([1, 2, 3]);
        var retiring = factory.RetireAsync();
        var disposing = capture.DisposeAsync().AsTask();
        await native.Microphones.Single().Released.Task.WaitAsync(TestContext.Current.CancellationToken);
        factory.IsAvailable.Should().BeFalse();
        factory.IsQuiescent.Should().BeFalse();
        retiring.IsCompleted.Should().BeFalse();
        disposing.IsCompleted.Should().BeFalse();
        stream.BufferedBytes.Should().Be(0);
        using var deniedStream = new BlockingAudioStream();
        await FluentActions.Awaiting(() => Open(factory, deniedStream)).Should().ThrowAsync<InvalidOperationException>();
        var engine = native.Engines.Single();
        engine.DisposeCount.Should().Be(0);
        engine.Complete();
        await disposing;
        await retiring;
        engine.DisposeCount.Should().Be(1);
        factory.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("dispose")]
    [InlineData("microphone")]
    [InlineData("completion")]
    public async Task UnconfirmedNativeReleaseQuarantinesInputWithoutFallback(string failure)
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        using var stream = new BlockingAudioStream();
        var capture = await Open(factory, stream);
        capture.Start();
        var engine = native.Engines.Single();
        engine.FailReset = failure is "reset";
        engine.FailDispose = failure is "dispose";
        native.Microphones.Single().FailRelease = failure is "microphone";
        if (failure is "completion") { engine.Complete(new InvalidOperationException("Native completion was not successful.")); }
        var retiring = factory.RetireAsync();
        await FluentActions.Awaiting(() => capture.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => retiring).Should().ThrowAsync<InvalidOperationException>();
        factory.IsAvailable.Should().BeFalse();
        factory.IsQuiescent.Should().BeFalse();
        using var deniedStream = new BlockingAudioStream();
        await FluentActions.Awaiting(() => Open(factory, deniedStream)).Should().ThrowAsync<InvalidOperationException>();
        native.Engines.Should().ContainSingle();
        native.Microphones.Should().ContainSingle();
        stream.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentCapturesCannotDoubleOwnTheModel()
    {
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        using var stream = new BlockingAudioStream();
        var capture = await Open(factory, stream);
        using var other = new BlockingAudioStream();
        await FluentActions.Awaiting(() => Open(factory, other)).Should().ThrowAsync<InvalidOperationException>();
        native.Engines.Should().ContainSingle();
        native.Microphones.Should().ContainSingle();
        await capture.DisposeAsync();
        await factory.RetireAsync();
    }

    [Fact]
    public async Task ProductionQualificationAndRevokedAdmissionCannotOpenOrEagerlyLoad()
    {
        var native = new Native { IsKeepLoadedQualified = false };
        var factory = new WindowsActivatedCaptureFactory(native);
        new WindowsActivatedCaptureFactory().GetUnavailableReason(KeepLoaded).Should().NotBeNull();
        using var stream = new BlockingAudioStream();
        await FluentActions.Awaiting(() => Open(factory, stream)).Should().ThrowAsync<InvalidOperationException>();
        native.IsKeepLoadedQualified = true;
        await FluentActions.Awaiting(() => factory.OpenAsync(Microphone, ["help"], stream, () => false,
            TimeSpan.FromSeconds(5), new(1, KeepLoaded))).Should().ThrowAsync<OperationCanceledException>();
        native.Engines.Should().BeEmpty();
        native.Microphones.Should().BeEmpty();
        await factory.RetireAsync();
    }

    [Fact]
    public async Task ServiceStopPrivacyProfileCutoverAndShutdownRetireModelOnlyState()
    {
        using var listener = Listen();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Runtime);
        var native = new Native();
        var factory = new WindowsActivatedCaptureFactory(native);
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        var service = new WindowsVoiceRecognitionService(Logger, privacy, factory, VoiceCaptureLimits.Default);
        try
        {
            service.InitializeProfile(KeepLoaded);
            native.Engines.Should().BeEmpty();
            service.IsListening.Should().BeFalse();
            for (var index = 0; index < 2; index++)
            {
                await service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
                await service.EndPushToTalkAsync(TestContext.Current.CancellationToken);
            }
            native.Engines.Should().ContainSingle();
            native.Microphones.Should().HaveCount(2).And.OnlyContain(microphone => microphone.Released.Task.IsCompletedSuccessfully);
            privacy.Set(privacy.Current with { SessionState = WindowsSessionState.Locked });
            await service.StopAsync(TestContext.Current.CancellationToken);
            native.Engines.Single().DisposeCount.Should().Be(1);
            privacy.Set(privacy.Current with { SessionState = WindowsSessionState.Unlocked });
            service.IsListening.Should().BeFalse();
            await service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
            await service.EndPushToTalkAsync(TestContext.Current.CancellationToken);
            await using (var switching = await service.RetireProfileAsync(TestContext.Current.CancellationToken))
            {
                native.Engines[1].DisposeCount.Should().Be(1);
                switching.Publish(new(1, SpeechInputSelection.Default));
            }
            service.ProfileGeneration.Should().Be(new SpeechInputGeneration(1, SpeechInputSelection.Default));
            native.Engines.Should().HaveCount(2);
            await service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
            await service.EndPushToTalkAsync(TestContext.Current.CancellationToken);
            native.Engines[2].DisposeCount.Should().Be(1);
            await using (var switching = await service.RetireProfileAsync(TestContext.Current.CancellationToken))
            { switching.Publish(new(2, KeepLoaded)); }
            await service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
            await service.EndPushToTalkAsync(TestContext.Current.CancellationToken);
            native.Engines[3].DisposeGate = Signal();
            var shutdown = service.DisposeAsync().AsTask();
            await native.Engines[3].DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            shutdown.IsCompleted.Should().BeFalse();
            native.Engines[3].DisposeGate!.SetResult();
            await shutdown;
            native.Engines.Should().OnlyContain(engine => engine.DisposeCount == 1);
            activity.Complete(HostOperationOutcome.Completed);
        }
        finally
        {
            foreach (var engine in native.Engines) { engine.DisposeGate?.TrySetResult(); }
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task CancellingStopCannotReleaseOrReplaceAnUncooperativeRecognizer()
    {
        var native = new Native { CompleteOnFinish = false };
        var factory = new WindowsActivatedCaptureFactory(native);
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        await using var service = new WindowsVoiceRecognitionService(Logger, privacy, factory, VoiceCaptureLimits.Default);
        service.InitializeProfile(KeepLoaded);
        await service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stopping = service.StopAsync(cancellation.Token);
        await native.Microphones.Single().Released.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        try
        {
            await FluentActions.Awaiting(() => stopping).Should().ThrowAsync<OperationCanceledException>();
            service.IsCaptureQuiescent.Should().BeFalse();
            service.IsProfileAvailable.Should().BeFalse();
            await FluentActions.Awaiting(() => service.BeginPushToTalkAsync(Microphone, ["help"],
                cancellationToken: TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            native.Engines.Should().ContainSingle();
            native.Engines.Single().DisposeCount.Should().Be(0);
        }
        finally { native.Engines.Single().Complete(); }
        await service.StopAsync(TestContext.Current.CancellationToken);
        service.IsCaptureQuiescent.Should().BeTrue();
        native.Engines.Single().DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task UncertainNativeStartCannotBeTreatedAsAnUnstartedReusableModel()
    {
        var native = new Native { CompleteOnFinish = false };
        var factory = new WindowsActivatedCaptureFactory(native);
        using var stream = new BlockingAudioStream();
        var capture = await Open(factory, stream);
        var engine = native.Engines.Single();
        engine.FailStart = true;
        FluentActions.Invoking(capture.Start).Should().Throw<InvalidOperationException>();
        var disposing = capture.DisposeAsync().AsTask();
        await native.Microphones.Single().Released.Task.WaitAsync(TestContext.Current.CancellationToken);
        disposing.IsCompleted.Should().BeFalse();
        factory.IsAvailable.Should().BeFalse();
        engine.Complete();
        await disposing;
        engine.DisposeCount.Should().Be(1);
        factory.IsAvailable.Should().BeFalse();
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static Task<IActivatedCapture> Open(WindowsActivatedCaptureFactory factory, BlockingAudioStream stream,
        SpeechInputGeneration? profile = null) =>
        factory.OpenAsync(Microphone, ["help"], stream, () => true, TimeSpan.FromSeconds(5), profile ?? new(1, KeepLoaded));

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class Native : IWindowsActivatedNativeFactory
    {
        public bool IsKeepLoadedQualified { get; set; } = true;
        public bool CompleteOnFinish { get; set; } = true;
        public List<Engine> Engines { get; } = [];
        public List<Input> Microphones { get; } = [];
        public void RequireEligible(MicrophoneDevice microphone) { }
        public IWindowsActivatedRecognizer CreateRecognizer(IReadOnlyList<string> phrases, TimeSpan speechStartWindow)
        {
            var engine = new Engine(phrases, CompleteOnFinish);
            Engines.Add(engine);
            return engine;
        }
        public Task<IWindowsActivatedMicrophone> OpenMicrophoneAsync(MicrophoneDevice microphone, Func<bool> canOpen)
        {
            if (!canOpen()) { throw new OperationCanceledException(); }
            var input = new Input();
            Microphones.Add(input);
            return Task.FromResult<IWindowsActivatedMicrophone>(input);
        }
    }

    internal sealed class Engine(IReadOnlyList<string> phrases, bool completeOnFinish) : IWindowsActivatedRecognizer
    {
        public IReadOnlyList<string> Phrases { get; } = phrases.ToArray();
        public BlockingAudioStream? Stream { get; private set; }
        public TaskCompletionSource ResetEntered { get; } = Signal();
        public TaskCompletionSource DisposeEntered { get; } = Signal();
        public TaskCompletionSource? ResetGate { get; set; }
        public TaskCompletionSource? DisposeGate { get; set; }
        public bool FailReset { get; set; }
        public bool FailDispose { get; set; }
        public bool FailStart { get; set; }
        public int DisposeCount { get; private set; }
        public event EventHandler? SpeechDetected;
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<ActivatedHypothesisEventArgs>? Hypothesized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;
        public event EventHandler<WindowsRecognitionCompletedEventArgs>? Completed;
        public int SubscriberCount => (SpeechDetected?.GetInvocationList().Length ?? 0)
            + (TranscriptRecognized?.GetInvocationList().Length ?? 0) + (Hypothesized?.GetInvocationList().Length ?? 0)
            + (RecognitionFailed?.GetInvocationList().Length ?? 0) + (Completed?.GetInvocationList().Length ?? 0);
        public void BindInput(BlockingAudioStream stream) => Stream = stream;
        public void Start()
        {
            if (FailStart) { throw new InvalidOperationException("Native start outcome is unknown."); }
            SpeechDetected?.Invoke(this, EventArgs.Empty);
        }
        public void Finish(bool cancel) { if (completeOnFinish) { Complete(); } }
        public void Complete(Exception? error = null) => Completed?.Invoke(this, new(error));
        public Action QueueTranscript(string text)
        {
            var callback = TranscriptRecognized;
            return () => callback?.Invoke(this, new(text, 1));
        }
        public async Task ResetAsync()
        {
            ResetEntered.TrySetResult();
            if (ResetGate is not null) { await ResetGate.Task.ConfigureAwait(false); }
            if (FailReset) { throw new InvalidOperationException("Native reset was not confirmed."); }
            Stream = null;
        }
        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            if (DisposeGate is not null) { await DisposeGate.Task.ConfigureAwait(false); }
            if (FailDispose) { throw new InvalidOperationException("Native disposal was not confirmed."); }
            DisposeCount++;
        }
    }

    internal sealed class Input : IWindowsActivatedMicrophone
    {
        public event WindowsCaptureAudioAvailable? DataAvailable;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecordingFailed;
        public TaskCompletionSource Released { get; } = Signal();
        public bool FailRelease { get; set; }
        public void Start() { }
        public void Audio(byte[] bytes) => DataAvailable?.Invoke(bytes);
        public void Fail() => RecordingFailed?.Invoke(this, new("Native input failed."));
        public ValueTask DisposeAsync()
        {
            if (FailRelease) { throw new InvalidOperationException("Microphone release was not confirmed."); }
            Released.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
