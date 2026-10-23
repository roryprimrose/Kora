using AwesomeAssertions;

using System.Diagnostics;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection("Host tracing")]
public sealed partial class ActivatedVoiceRecognitionTests
{
    [Fact]
    public async Task ProfileSwitchJoinsCaptureAndRejectsQueuedLateAndDuplicateFinals()
    {
        using var listener = CreateProfileListener();
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture { DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);
        await BeginCaptureAsync(service);
        var captureGeneration = service.CaptureGeneration;
        var late = capture.QueueTranscript("Kora help");
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var switching = service.RetireProfileAsync(TestContext.Current.CancellationToken);
        service.IsListening.Should().BeFalse();
        late();
        late();
        results.Should().BeEmpty();
        switching.IsCompleted.Should().BeFalse();
        await FluentActions.Awaiting(() => service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        capture.DisposeGate.SetResult();
        await using (var retired = await switching)
        {
            service.IsCaptureQuiescent.Should().BeTrue();
            factory.Stream!.BufferedBytes.Should().Be(0);
            retired.Publish(new(1, SpeechInputSelection.Default with { Resource = SpeechInputResource.Gpu }));
            service.AcceptCaptureGeneration(captureGeneration).Should().BeFalse();
        }
        service.ProfileGeneration.Number.Should().Be(1);
        service.IsProfileAvailable.Should().BeFalse();
        await FluentActions.Awaiting(() => service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        factory.OpenCount.Should().Be(1);
        results.Should().BeEmpty();
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task SelectionNeverOpensCaptureAndExplicitBaselineRecoveryRetainsExistingPtt()
    {
        using var listener = CreateProfileListener();
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        service.InitializeProfile(SpeechInputSelection.Default with { Residency = SpeechInputResidency.KeepLoaded });
        factory.OpenCount.Should().Be(0);
        service.IsProfileAvailable.Should().BeFalse();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await using (var retirement = await service.RetireProfileAsync(TestContext.Current.CancellationToken))
        {
            retirement.Publish(new(1, SpeechInputSelection.Default));
        }
        factory.OpenCount.Should().Be(0);
        service.IsListening.Should().BeFalse();
        await BeginCaptureAsync(service);
        service.IsListening.Should().BeTrue();
        factory.OpenCount.Should().Be(1);
        service.IsAmbientListeningAvailable.Should().BeFalse();
        activity.Complete(HostOperationOutcome.Completed);
    }

    private static ActivityListener CreateProfileListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}