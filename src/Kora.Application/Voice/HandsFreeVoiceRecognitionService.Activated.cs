using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService
{
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
        Interlocked.Exchange(ref ambientGeneration, 0);
        await activated.BeginPushToTalkAsync(microphone, phrases, assistantName, cancellationToken);
    }

    public async Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
        Func<bool> isAdmitted, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        await StopAsync(cancellationToken);
        EnsureNotDisposed();
        Interlocked.Exchange(ref ambientGeneration, 0);
        await activated.BeginReplyAsync(microphone, phrases, isAdmitted, cancellationToken);
    }
}
