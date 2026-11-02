using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

[Collection("Host tracing")]
public sealed class SpeechSetupComparisonTests : IDisposable
{
    private readonly ActivityListener activityListener = Listen();
    internal static readonly SpeechComparisonProfile Profile = new(SpeechInputSelection.Default, "Installed test profile");
    private static readonly MicrophoneDevice Microphone = new("test", "Test microphone");

    [Fact]
    public async Task ReviewAndTwoInertCapturesPublishExactCorrectnessAndOnlyMeasuredStartupWithoutChangingProfile()
    {
        using var root = Root();
        var runtime = new Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var changes = 0;
        comparison.Changed += (_, _) => changes++;
        comparison.ExpectedPhrase = "Kora approve delete everything";
        comparison.ExpectedPhrase = comparison.ExpectedPhrase;
        runtime.Capture = (_, trial, _) => Task.FromResult(new SpeechComparisonObservation(
            trial == 1 ? "kora approve delete everything" : "different", TimeSpan.FromMilliseconds(trial * 25)));
        comparison.Review(Microphone, static () => true);
        comparison.ReviewDescription.Should().Contain(Profile.Name);
        comparison.Status.Should().Contain("Review");
        comparison.CanTest.Should().BeTrue();
        runtime.Acquisitions.Should().Be(0);
        await comparison.TestAsync(microphoneUseConfirmed: true);
        comparison.IsRunning.Should().BeFalse();
        comparison.Results.Should().HaveCount(2);
        comparison.Results[0].Correct.Should().BeTrue();
        comparison.Results[1].Correct.Should().BeFalse();
        comparison.Results[0].Description.Should().Contain("first").And.Contain("25.0 ms").And.Contain("Cold startup: unknown");
        comparison.Results[1].Description.Should().Contain("repeat").And.Contain("mismatch");
        var observation = comparison.Results[0].Observation;
        observation.EndOfSpeechToFinal.Should().BeNull();
        observation.ColdStartup.Should().BeNull();
        observation.WarmStartup.Should().BeNull();
        observation.ResourceBytes.Should().BeNull();
        runtime.Disposals.Should().Be(1);
        runtime.ProfileGeneration.Should().Be(new SpeechInputGeneration(0, SpeechInputSelection.Default));
        comparison.Status.Should().Contain("complete").And.Contain("unchanged");
        comparison.CanTest.Should().BeFalse();
        changes.Should().BeGreaterThan(2);
        comparison.Clear();
        comparison.Results.Should().BeEmpty();
        comparison.ExpectedPhrase.Should().Contain("quiet garden");
        await comparison.CancelAsync();
        comparison.Status.Should().Contain("Cancellation requested");
    }

    [Fact]
    public async Task EmptyCapabilitySetReportsUnavailableAndNeverAcquiresOrDownloads()
    {
        using var root = Root();
        var runtime = new Runtime { Offered = [] };
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        comparison.Review(Microphone, static () => true);
        comparison.CanTest.Should().BeFalse();
        comparison.ReviewDescription.Should().BeEmpty();
        comparison.Status.Should().Contain("Unavailable").And.Contain("No download");
        await FluentActions.Awaiting(() => comparison.TestAsync(true)).Should().ThrowAsync<InvalidOperationException>();
        runtime.Acquisitions.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("line\nbreak")]
    [InlineData("too-long")]
    public async Task InvalidPhraseCannotBeReviewed(string phrase)
    {
        using var root = Root();
        var runtime = new Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        comparison.ExpectedPhrase = string.Equals(phrase, "too-long", StringComparison.Ordinal) ? new string('a', 257) : phrase;
        FluentActions.Invoking(() => comparison.Review(Microphone, static () => true)).Should().Throw<InvalidOperationException>();
        runtime.Acquisitions.Should().Be(0);
    }

    [Theory]
    [InlineData("duplicates")]
    [InlineData("limit")]
    [InlineData("unsupported")]
    public async Task InvalidCapabilitySetsCannotBeReviewed(string mode)
    {
        using var root = Root();
        var runtime = new Runtime
        {
            Offered = mode switch
            {
                "duplicates" => [Profile, Profile],
                "limit" => [Profile, Profile, Profile],
                _ => [Profile with { Selection = SpeechInputSelection.Default with { Resource = SpeechInputResource.Gpu } }],
            },
        };
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        FluentActions.Invoking(() => comparison.Review(Microphone, static () => true)).Should().Throw<InvalidOperationException>();
        runtime.Acquisitions.Should().Be(0);
    }

    [Theory]
    [InlineData("phrase")]
    [InlineData("host")]
    [InlineData("profile")]
    [InlineData("catalogue")]
    [InlineData("availability")]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task RevocationClearsResultsAndRetainsOwnershipUntilCancellationIgnoringAttemptJoins(string reason)
    {
        using var root = Root();
        var runtime = new Runtime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SpeechComparisonObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captureToken = default;
        runtime.Capture = (_, _, token) => { captureToken = token; entered.TrySetResult(); return release.Task; };
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var admitted = true;
        comparison.Review(Microphone, () => admitted);
        var work = comparison.TestAsync(true);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        comparison.IsRunning.Should().BeTrue();
        FluentActions.Invoking(() => comparison.Review(Microphone, () => admitted)).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => comparison.TestAsync(true)).Should().ThrowAsync<InvalidOperationException>();
        Task? joining = null;
        switch (reason)
        {
            case "phrase": comparison.ExpectedPhrase = "Changed"; break;
            case "host": admitted = false; comparison.Revalidate(); break;
            case "profile": runtime.ProfileGeneration = new(1, SpeechInputSelection.Default); comparison.Revalidate(); break;
            case "catalogue": runtime.Offered = []; comparison.Revalidate(); break;
            case "availability": runtime.IsProfileAvailable = false; comparison.Revalidate(); break;
            case "close": comparison.Clear(); break;
            case "dispose": joining = comparison.DisposeAsync().AsTask(); break;
            default: joining = comparison.CancelAsync(); break;
        }
        captureToken.IsCancellationRequested.Should().BeTrue();
        work.IsCompleted.Should().BeFalse();
        if (joining is not null) { joining.IsCompleted.Should().BeFalse(); }
        comparison.Results.Should().BeEmpty();
        runtime.Disposals.Should().Be(0);
        release.SetResult(new("Late inert result", TimeSpan.FromMilliseconds(50)));
        await work;
        if (joining is not null) { await joining; }
        runtime.Captures.Should().Be(1);
        runtime.Disposals.Should().Be(1);
        comparison.Results.Should().BeEmpty();
        comparison.CanTest.Should().BeFalse();
        comparison.IsRunning.Should().BeFalse();
        comparison.Status.Should().Contain("cancelled");
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("acquire")]
    [InlineData("length")]
    [InlineData("timing")]
    public async Task FailedOrInvalidObservationsNeverPublishSuccess(string reason)
    {
        using var root = Root();
        var runtime = new Runtime { FailAcquire = string.Equals(reason, "acquire", StringComparison.Ordinal) };
        runtime.Capture = (_, _, _) => reason switch
        {
            "capture" => Task.FromException<SpeechComparisonObservation>(new IOException("Synthetic failure")),
            "length" => Task.FromResult(new SpeechComparisonObservation(new string('x', 257), TimeSpan.Zero)),
            _ => Task.FromResult(new SpeechComparisonObservation("text", TimeSpan.FromMilliseconds(-1))),
        };
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        comparison.Review(Microphone, static () => true);
        await comparison.TestAsync(true);
        comparison.Results.Should().BeEmpty();
        comparison.Status.Should().Contain("failed").And.Contain("No success");
        comparison.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task OriginalInputConsentAndExplicitTestAreRequiredAndDisposedStateCannotRevive()
    {
        var runtime = new Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        using (var system = Root(RequestOrigin.HostSystem))
        {
            FluentActions.Invoking(() => comparison.Review(Microphone, static () => true)).Should().Throw<InvalidOperationException>();
        }
        using var root = Root();
        FluentActions.Invoking(() => comparison.Review(Microphone, static () => false)).Should().Throw<InvalidOperationException>();
        runtime.IsProfileAvailable = false;
        FluentActions.Invoking(() => comparison.Review(Microphone, static () => true)).Should().Throw<InvalidOperationException>();
        runtime.IsProfileAvailable = true;
        comparison.Review(Microphone, static () => true);
        comparison.Revalidate();
        comparison.CanTest.Should().BeTrue();
        await FluentActions.Awaiting(() => comparison.TestAsync(false)).Should().ThrowAsync<InvalidOperationException>();
        await comparison.DisposeAsync();
        FluentActions.Invoking(() => comparison.Review(Microphone, static () => true)).Should().Throw<ObjectDisposedException>();
        await FluentActions.Awaiting(() => comparison.TestAsync(true)).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task TestRequiresFreshLocalOriginAndStaleUnnotifiedReviewCannotCapture(RequestOrigin origin)
    {
        var runtime = new Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var eligible = true;
        using (var review = Root()) { comparison.Review(Microphone, () => eligible); }
        using var root = Root(origin);
        if (origin == RequestOrigin.HostSystem)
        { await FluentActions.Awaiting(() => comparison.TestAsync(true)).Should().ThrowAsync<InvalidOperationException>(); }
        else
        {
            eligible = false;
            await FluentActions.Awaiting(() => comparison.TestAsync(true)).Should().ThrowAsync<InvalidOperationException>();
            eligible = true;
            await comparison.TestAsync(true);
            comparison.Review(Microphone, () => eligible);
            runtime.AfterAcquire = () => eligible = false;
            await comparison.TestAsync(true);
            comparison.Results.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmissionLostAfterCaptureOrLeaseJoinCannotPublishSuccess(bool afterJoin)
    {
        using var root = Root();
        var eligible = true;
        var runtime = new Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        if (afterJoin) { runtime.AfterDispose = () => eligible = false; }
        else { runtime.Capture = (_, _, _) => { eligible = false; return Task.FromResult(new SpeechComparisonObservation("late", TimeSpan.Zero)); }; }
        comparison.Review(Microphone, () => eligible);
        await comparison.TestAsync(true);
        comparison.Results.Should().BeEmpty();
        comparison.Status.Should().Contain("cancelled");
    }

    [Fact]
    public async Task RecordingReadinessIsTruthfulAndLateOrRevokedReadinessCannotReplaceTerminalStatus()
    {
        using var root = Root();
        var runtime = new Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var callbacks = new List<Action>();
        var admitted = true;
        runtime.OnRecording = callback =>
        {
            comparison.Status.Should().StartWith("Speak now");
            callbacks.Add(callback);
        };
        comparison.Review(Microphone, () => admitted);
        comparison.ReviewDescription.Should().Contain(Microphone.Name);
        await comparison.TestAsync(true);
        var terminal = comparison.Status;
        foreach (var callback in callbacks) { callback(); }
        comparison.Status.Should().Be(terminal);
        comparison.Review(Microphone, () => admitted);
        runtime.OnRecording = callback =>
        {
            admitted = false;
            callback();
        };
        await comparison.TestAsync(true);
        comparison.Results.Should().BeEmpty();
        comparison.Status.Should().Contain("cancelled");
    }

    [Fact]
    public async Task UnexpectedRuntimeFailuresPropagateInsteadOfBecomingSuccessfulEvidence()
    {
        using var root = Root();
        var runtime = new Runtime
        {
            Capture = static (_, _, _) => Task.FromException<SpeechComparisonObservation>(new ArgumentException("Synthetic contract failure")),
        };
        var comparison = new SpeechSetupComparison(runtime, runtime);
        comparison.Review(Microphone, static () => true);
        await FluentActions.Awaiting(() => comparison.TestAsync(true)).Should().ThrowAsync<ArgumentException>();
        comparison.Results.Should().BeEmpty();
        await FluentActions.Awaiting(() => comparison.DisposeAsync().AsTask()).Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("compare speech input", "compare speech input")]
    [InlineData(" Kora, TEST SPEECH COMPARISON ", "test speech comparison")]
    [InlineData("Kora cancel speech comparison", "cancel speech comparison")]
    [InlineData("compare speech input extra", null)]
    [InlineData("approve speech comparison", null)]
    public void ExactEntryCommandsDoNotParseCalibrationTextAsAuthority(string input, string? operation) =>
        (SpeechComparisonCommand.Parse(input, "Kora")?.Operation).Should().Be(operation);

    public void Dispose() => activityListener.Dispose();
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

    private static HostActivity Root(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);

    internal sealed class Runtime : ISpeechSetupComparisonRuntime, ISpeechInputProfileRuntime
    {
        public IReadOnlyList<SpeechComparisonProfile> Offered { get; set; } = [Profile];
        public SpeechInputGeneration ProfileGeneration { get; set; } = new(0, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; set; } = true;
        public int Acquisitions { get; private set; }
        public int Captures { get; private set; }
        public int Disposals { get; private set; }
        public bool FailAcquire { get; set; }
        public Action? AfterAcquire { get; set; }
        public Action? AfterDispose { get; set; }
        public Action<Action>? OnRecording { get; set; }
        public Func<SpeechComparisonProfile, int, CancellationToken, Task<SpeechComparisonObservation>> Capture { get; set; } =
            static (_, _, _) => Task.FromResult(new SpeechComparisonObservation("text", TimeSpan.Zero));
        public IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() => Offered;
        public Task<ISpeechSetupComparisonLease> AcquireComparisonAsync(SpeechInputGeneration generation,
            MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken)
        {
            Acquisitions++;
            generation.Should().Be(ProfileGeneration);
            eligible().Should().BeTrue();
            if (FailAcquire) { throw new InvalidOperationException("Synthetic unavailable"); }
            AfterAcquire?.Invoke();
            return Task.FromResult<ISpeechSetupComparisonLease>(new Lease(this));
        }
        public void InitializeProfile(SpeechInputSelection? selection) => throw new NotSupportedException();
        public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        private sealed class Lease(Runtime owner) : ISpeechSetupComparisonLease
        {
            public Task<SpeechComparisonObservation> CaptureAsync(SpeechComparisonProfile profile, Action recordingStarted,
                CancellationToken cancellationToken)
            {
                recordingStarted();
                owner.OnRecording?.Invoke(recordingStarted);
                return owner.Capture(profile, ++owner.Captures, cancellationToken);
            }
            public ValueTask DisposeAsync() { owner.Disposals++; owner.AfterDispose?.Invoke(); return ValueTask.CompletedTask; }
        }
    }
}
