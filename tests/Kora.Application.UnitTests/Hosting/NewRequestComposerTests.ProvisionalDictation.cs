using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    private static ComposerDictationTarget SessionTarget(DictationFixture f)
    {
        var original = f.Host.Composer.CaptureDictationTarget();
        return new(original.Source, original.Body, original.Request, original.IsCurrent, original.Insert,
            ComposerDictationDestination.SessionComposer);
    }

    [Fact]
    public async Task ProvisionalSnapshotsCoalesceOnePendingUiWorkItemWithoutMutatingOriginalOrDispatching()
    {
        await using var f = new DictationFixture(log: true);
        var target = SessionTarget(f);
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        var late = f.Capture.Observer!;
        f.Dispatcher.QueuePosts = true;
        for (var i = 1; i <= 1000; i++) { f.Capture.Hypothesis("private " + i, i); }
        f.Dispatcher.Posts.Count.Should().Be(1);
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Dispatcher.Flush();
        f.Service.ProvisionalTextFor(target).Should().Be("private 1000");
        f.Service.ProvisionalTextFor(SessionTarget(f)).Should().BeEmpty("a different presenter has no read authority");
        f.Capture.Hypothesis("old", 999);
        f.Capture.Hypothesis("duplicate", 1000);
        f.Capture.Hypothesis("foreign", 1001, f.Capture.Generation + 1);
        f.Dispatcher.Posts.Should().BeEmpty();
        f.Host.Composer.Body.Should().BeEmpty();
        f.Host.Creates.Should().Be(0);
        f.Host.ModelAdmissions.Should().Be(0);
        f.Host.Retentions.Should().Be(0);
        f.Messages.Should().BeEmpty();
        f.Capture.Hypothesis("pending before final", 1001);
        f.Capture.Transcript("accepted final only");
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Capture.Observer.Should().BeNull();
        f.Dispatcher.Flush();
        late(new(f.Capture.Generation, 1002, "late private"));
        await f.Service.EndAsync();
        await f.Service.Quiescence;
        f.Dispatcher.Flush();
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Host.Composer.Body.Should().Be("accepted final only");
        f.Capture.ReleasedObservations.Should().Be(1);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        var replacement = SessionTarget(f);
        await f.Service.BeginAsync(replacement, () => f.Native, TestContext.Current.CancellationToken);
        f.Capture.Hypothesis("new current capture");
        f.Dispatcher.Flush();
        late(new(f.Capture.Generation, 2000, "old presenter cannot borrow a new generation"));
        f.Service.ProvisionalTextFor(replacement).Should().Be("new current capture");
        f.Service.Retire("Explicit cancel");
        await f.Service.Quiescence;
        f.Dispatcher.Flush();
    }

    [Theory]
    [InlineData("high")]
    [InlineData("low")]
    [InlineData("\0")]
    [InlineData("characters")]
    [InlineData("bytes")]
    public async Task InvalidOrOversizedSnapshotVisiblyRetiresPreviewButNotFinalDictation(string invalid)
    {
        await using var f = new DictationFixture(log: true);
        var target = SessionTarget(f);
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        f.Capture.Hypothesis("valid");
        f.Service.ProvisionalTextFor(target).Should().Be("valid");
        var text = invalid switch { "characters" => new('x', 513), "bytes" => new('\u0800', 342),
            "high" => "\ud800", "low" => "\udc00", _ => invalid };
        f.Capture.Hypothesis(text, 2);
        f.Service.Status.Should().Contain("invalid or oversized");
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Capture.Hypothesis(invalid is "high" ? "\udc00" : "cannot revive", 3);
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Capture.Transcript("valid final");
        await f.Service.EndAsync();
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().Be("valid final");
        f.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("same-edit")]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("control")]
    [InlineData("control-revision")]
    [InlineData("private")]
    [InlineData("native")]
    [InlineData("close")]
    [InlineData("recovery")]
    [InlineData("topology")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("microphone")]
    [InlineData("qualification")]
    [InlineData("generation")]
    public async Task EveryDictationRetirementClearsAndFencesQueuedPrivateHypotheses(string boundary)
    {
        await using var f = new DictationFixture();
        var target = SessionTarget(f);
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        f.Capture.Hypothesis("visible");
        f.Service.ProvisionalTextFor(target).Should().Be("visible");
        var late = f.Capture.Observer!;
        var generation = f.Capture.Generation;
        f.Dispatcher.QueuePosts = true;
        f.Capture.Hypothesis("queued", 2);
        f.Change(boundary);
        f.Service.Revalidate();
        if (f.Service.IsActive) { f.Service.Retire("Explicit cancellation"); }
        late(new(generation, 3, "late"));
        f.Dispatcher.Flush();
        await f.Service.Quiescence;
        f.Service.IsActive.Should().BeFalse();
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Capture.ReleasedObservations.Should().Be(1);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Host.ModelAdmissions.Should().Be(0);
        f.Host.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalOnlyAndNonConversationCaptureNeverManufactureFeedback(bool conversation)
    {
        await using var f = new DictationFixture();
        f.Capture.SupportsProvisional = false;
        var target = conversation ? SessionTarget(f) : f.Host.Composer.CaptureDictationTarget();
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        f.Capture.Observations.Should().Be(conversation ? 1 : 0);
        if (conversation) { f.Service.Status.Should().Contain("Provisional feedback unavailable"); }
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Capture.Transcript("exact final");
        await f.Service.EndAsync();
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().Be("exact final");
    }

    [Fact]
    public async Task ObservationOpeningRevocationAndEmptySnapshotsNeverLeakOrHoldSubscription()
    {
        await using var f = new DictationFixture();
        var target = SessionTarget(f);
        f.Capture.DuringObservation = () => f.Service.Retire("Native arm revoked");
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        await f.Service.Quiescence;
        f.Capture.Observer.Should().BeNull();
        f.Capture.DuringObservation = null;
        target = SessionTarget(f);
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        f.Capture.Hypothesis("first");
        f.Capture.Hypothesis("", 2);
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Dispatcher.QueuePosts = true;
        f.Capture.Hypothesis("queued", 3);
        await f.Service.EndAsync();
        f.Dispatcher.Flush();
        await f.Service.Quiescence;
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
    }

    [Theory]
    [InlineData(VoiceRecognitionCompletionReason.Recognized)]
    [InlineData(VoiceRecognitionCompletionReason.PushToTalkReleased)]
    [InlineData(VoiceRecognitionCompletionReason.MaximumDuration)]
    [InlineData(VoiceRecognitionCompletionReason.EmptySpeechTimeout)]
    public async Task RecognitionCompletionRetiresFeedbackEvenWhenFinalDeliveryHasNotArrived(VoiceRecognitionCompletionReason reason)
    {
        await using var f = new DictationFixture();
        var target = SessionTarget(f);
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        f.Capture.Hypothesis("private");
        f.Dispatcher.QueuePosts = true;
        f.Capture.Hypothesis("queued", 2);
        f.Capture.Complete(reason);
        f.Dispatcher.Flush();
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Capture.Observer.Should().BeNull();
        f.Service.Retire("Explicit cleanup");
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task AProviderWithoutTheOptionalCapabilityStillPublishesOnlyItsAcceptedFinal()
    {
        using var host = new Fixture();
        var capture = new DictationCapture();
        await using var service = new ComposerDictationService(capture, new DictationDispatcher(),
            () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1), NullLogger<ComposerDictationService>.Instance);
        var original = host.Composer.CaptureDictationTarget();
        var target = new ComposerDictationTarget(original.Source, original.Body, original.Request, original.IsCurrent,
            original.Insert, ComposerDictationDestination.SessionComposer);
        await service.BeginAsync(target, static () => true, TestContext.Current.CancellationToken);
        service.Status.Should().Contain("Provisional feedback unavailable");
        service.ProvisionalTextFor(target).Should().BeEmpty();
        capture.Transcript("final-only provider");
        await service.EndAsync();
        await service.Quiescence;
        host.Composer.Body.Should().Be("final-only provider");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeArmRevokedDuringOptionalObservationOpeningClosesAndJoinsCapture(bool supported)
    {
        await using var f = new DictationFixture();
        f.Capture.SupportsProvisional = supported;
        f.Capture.DuringObservation = () => f.Native = false;
        var target = SessionTarget(f);
        await f.Service.BeginAsync(target, () => f.Native, TestContext.Current.CancellationToken);
        await f.Service.Quiescence;
        f.Service.IsActive.Should().BeFalse();
        f.Service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }
}
