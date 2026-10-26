using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed partial class FakeVoiceRecognitionService : IProvisionalVoiceRecognitionService
    {
        public Action<ProvisionalTranscriptSnapshot>? DictationObserver { get; private set; }
        public IDisposable? ObserveProvisionalTranscripts(long generation, Action<ProvisionalTranscriptSnapshot> observer)
        {
            DictationObserver = observer;
            return new DictationObservation(this);
        }

        private sealed class DictationObservation(FakeVoiceRecognitionService owner) : IDisposable
        {
            public void Dispose() => owner.DictationObserver = null;
        }
    }
}
