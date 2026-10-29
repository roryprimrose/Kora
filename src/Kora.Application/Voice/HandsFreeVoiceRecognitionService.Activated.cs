using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService : IProvisionalVoiceRecognitionService
{
    public bool IsLiteralDictationAvailable => activated.IsLiteralDictationAvailable;

    public IDisposable? ObserveProvisionalTranscripts(long generation, Action<ProvisionalTranscriptSnapshot> observer)
    {
        EnsureNotDisposed();
        return Interlocked.Read(ref ambientGeneration) == 0 && activated is IProvisionalVoiceRecognitionService source
            ? source.ObserveProvisionalTranscripts(generation, observer) : null;
    }

    public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized
    {
        add => activated.TranscriptRecognized += value;
        remove => activated.TranscriptRecognized -= value;
    }

    public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed
    {
        add => activated.RecognitionFailed += value;
        remove => activated.RecognitionFailed -= value;
    }

    public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged
    {
        add => activated.CaptureStateChanged += value;
        remove => activated.CaptureStateChanged -= value;
    }

    public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted
    {
        add => activated.RecognitionCompleted += value;
        remove => activated.RecognitionCompleted -= value;
    }

    public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted
    {
        add => activated.ReplySpeechStarted += value;
        remove => activated.ReplySpeechStarted -= value;
    }

    public IReadOnlyList<MicrophoneDevice> GetMicrophones() => activated.GetMicrophones();
    public MicrophoneDevice? GetDefaultMicrophone() => activated.GetDefaultMicrophone();
    public bool AcceptCaptureGeneration(long generation) => activated.AcceptCaptureGeneration(generation);
    public Task EndCaptureAsync(CancellationToken cancellationToken = default) => activated.EndCaptureAsync(cancellationToken);
    public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) => activated.EndPushToTalkAsync(cancellationToken);

    public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
        string? assistantName = null, CancellationToken cancellationToken = default) =>
        BeginPushToTalkAsync(microphone, phrases, assistantName, cancellationToken);

    public async Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
        string? assistantName = null, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        await StopAsync(cancellationToken);
        EnsureNotDisposed();
        Task opening;
        lock (gate)
        {
            if (profileSwitchActive || profileRetirementFailed) { throw new InvalidOperationException("Speech-input switching or unconfirmed retirement owns activated capture."); }
            Interlocked.Exchange(ref ambientGeneration, 0);
            opening = activated.BeginPushToTalkAsync(microphone, phrases, assistantName, cancellationToken);
        }
        await opening;
    }

    public async Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
        Func<bool> isAdmitted, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        await StopAsync(cancellationToken);
        EnsureNotDisposed();
        Task opening;
        lock (gate)
        {
            if (profileSwitchActive || profileRetirementFailed) { throw new InvalidOperationException("Speech-input switching or unconfirmed retirement owns reply capture."); }
            Interlocked.Exchange(ref ambientGeneration, 0);
            opening = activated.BeginReplyAsync(microphone, phrases, isAdmitted, cancellationToken);
        }
        await opening;
    }
}
