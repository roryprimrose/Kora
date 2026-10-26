using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalActivatedHypothesesDoNotEnableOrObserveWakeAndPreserveFinalOnlyProviders(bool supported)
    {
        Activated native = supported ? new ProvisionalActivated() : new Activated();
        native.IsLiteralDictationAvailable = supported;
        Action<ProvisionalTranscriptSnapshot> observer = _ => { };
        await using var voice = new HandsFreeVoiceRecognitionService(native, new Stage(),
            NullLogger<HandsFreeVoiceRecognitionService>.Instance);
        voice.IsLiteralDictationAvailable.Should().Be(supported);
        voice.ObserveProvisionalTranscripts(7, observer).Should().BeSameAs((native as ProvisionalActivated)?.Observation);
        voice.InvalidateCapture();
        voice.ObserveProvisionalTranscripts(7, observer).Should().BeNull("ambient generations never reach the activated observer");
    }

    private sealed class ProvisionalActivated : Activated, IProvisionalVoiceRecognitionService
    {
        public IDisposable Observation { get; } = new EmptyObservation();
        public IDisposable? ObserveProvisionalTranscripts(long generation, Action<ProvisionalTranscriptSnapshot> observer) => Observation;
        private sealed class EmptyObservation : IDisposable { public void Dispose() { } }
    }
}
