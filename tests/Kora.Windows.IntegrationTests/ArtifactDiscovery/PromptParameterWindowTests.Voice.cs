using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class PromptParameterWindowTests
{
    private sealed class PromptCapture : IQuestionVoiceRecognitionService
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted { add { } remove { } }
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted { add { } remove { } }
        public bool IsListening { get; private set; }
        public long Generation { get; private set; }
        public long CaptureGeneration => Generation;
        public long LastDeliveredGeneration { get; private set; }
        public bool IsAmbientListeningAvailable => false;
        public bool IsCaptureQuiescent => !IsListening;
        public bool AcceptCaptureGeneration(long generation) => generation == Generation;
        public Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
            Func<bool> isAdmitted, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!isAdmitted()) { throw new InvalidOperationException("Synthetic prompt capture is not admitted."); }
            Generation++;
            IsListening = true;
            return Task.CompletedTask;
        }
        public void Transcript(string text, long? generation = null)
        {
            var exact = generation ?? Generation;
            LastDeliveredGeneration = exact;
            TranscriptRecognized?.Invoke(this, new(text, 1, exact));
        }
        public void InvalidateCapture() { Generation++; IsListening = false; }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsListening = false; return Task.CompletedTask; }
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not command capture.");
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not command capture.");
        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not PTT capture.");
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not command capture.");
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => [];
        public MicrophoneDevice? GetDefaultMicrophone() => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
