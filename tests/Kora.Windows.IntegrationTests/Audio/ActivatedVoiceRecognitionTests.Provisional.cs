using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Windows.Audio;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed partial class ActivatedVoiceRecognitionTests
{
    [Fact]
    public async Task OptionalHypothesesRequireAcknowledgedExactNativeCaptureAndNeverBecomeFinalTranscript()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var native = new FakeCapture();
        factory.OpenResult.SetResult(native);
        await using var service = Create(privacy, factory);
        var snapshots = new List<ProvisionalTranscriptSnapshot>();
        var transcripts = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        service.ObserveProvisionalTranscripts(0, snapshots.Add).Should().BeNull();
        await service.BeginPushToTalkAsync(Microphone, [], cancellationToken: TestContext.Current.CancellationToken);
        var generation = service.CaptureGeneration;
        service.ObserveProvisionalTranscripts(generation, snapshots.Add).Should().BeNull();
        service.AcceptCaptureGeneration(generation).Should().BeTrue();
        service.ObserveProvisionalTranscripts(generation + 1, snapshots.Add).Should().BeNull();
        using var observation = service.ObserveProvisionalTranscripts(generation, snapshots.Add);
        observation.Should().NotBeNull();
        var duplicate = () => service.ObserveProvisionalTranscripts(generation, snapshots.Add);
        duplicate.Should().Throw<InvalidOperationException>();
        native.Hypothesis("foreign", sender: new object());
        native.Hypothesis("PRIVATE_SNAPSHOT", 2);
        snapshots.Should().ContainSingle().Which.Should().Be(new ProvisionalTranscriptSnapshot(generation, 2, "PRIVATE_SNAPSHOT"));
        transcripts.Should().BeEmpty();
        var late = native.QueueHypothesis("PRIVATE_LATE", 3);
        observation!.Dispose();
        late();
        snapshots.Should().ContainSingle();
        using var replacement = service.ObserveProvisionalTranscripts(generation, snapshots.Add);
        replacement.Should().NotBeNull();
        native.Transcript("final");
        native.Hypothesis("PRIVATE_AFTER_FINAL", 4);
        snapshots.Should().ContainSingle();
        transcripts.Should().ContainSingle().Which.Transcript.Should().Be("final");
        await service.StopAsync(TestContext.Current.CancellationToken);
        service.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task HypothesisConsumerFailureIsContentFreeAndRetiresOnlyObservation()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var native = new FakeCapture();
        factory.OpenResult.SetResult(native);
        var messages = new List<string>();
        await using var service = new WindowsVoiceRecognitionService(new HypothesisLogger(messages), privacy, factory, VoiceCaptureLimits.Default);
        await BeginCaptureAsync(service);
        using var observation = service.ObserveProvisionalTranscripts(service.Generation,
            _ => throw new InvalidOperationException("PRIVATE_EXCEPTION_CONTENT"));
        native.Hypothesis("PRIVATE_SNAPSHOT");
        service.IsListening.Should().BeTrue();
        using var second = service.ObserveProvisionalTranscripts(service.Generation, _ => { });
        second.Should().NotBeNull();
        messages.Should().Contain(message => message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal))
            .And.NotContain(message => message.Contains("PRIVATE", StringComparison.Ordinal));
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class HypothesisLogger(List<string> messages) : ILogger<WindowsVoiceRecognitionService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
    }
}
