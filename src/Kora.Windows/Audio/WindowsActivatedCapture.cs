using System.Speech.Recognition;

using Kora.Core.Voice;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kora.Windows.Audio;

internal sealed class WindowsActivatedCapture : IActivatedCapture
{
    private readonly Lock sync = new();
    private readonly Lock releaseSync = new();
    private readonly SpeechRecognitionEngine recognizer;
    private readonly WasapiRecorder recorder;
    private readonly MMDevice? endpoint;
    private readonly MicrophoneDevice microphone;
    private readonly Func<bool> canStart;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? recorderRelease;
    private bool started;
    private int releaseRequested;

    public WindowsActivatedCapture(
        SpeechRecognitionEngine recognizer,
        WasapiRecorder recorder,
        MMDevice? endpoint,
        MicrophoneDevice microphone,
        Func<bool> canStart)
    {
        this.recognizer = recognizer;
        this.recorder = recorder;
        this.endpoint = endpoint;
        this.microphone = microphone;
        this.canStart = canStart;
        recognizer.SpeechDetected += OnSpeechDetected;
        recognizer.SpeechRecognized += OnSpeechRecognized;
        recognizer.SpeechRecognitionRejected += OnSpeechRejected;
        recognizer.RecognizeCompleted += OnCompleted;
        recorder.DataAvailable += OnDataAvailable;
        recorder.RecordingStopped += OnRecordingStopped;
    }

    public event ActivatedAudioAvailable? DataAvailable;

    public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

    public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

    public event EventHandler? SpeechDetected;

    public Task Completion => completion.Task;

    public void Start()
    {
        if (!canStart())
        {
            throw new OperationCanceledException("The native capture generation is no longer current.");
        }

        WindowsCapturePrivacyGate.RequireEligible(microphone);
        lock (sync)
        {
            if (Volatile.Read(ref releaseRequested) != 0)
            {
                throw new InvalidOperationException("Capture was invalidated before recording began.");
            }

            recognizer.RecognizeAsync(RecognizeMode.Single);
            started = true;
            recorder.StartRecording();
        }
    }

    public Task ReleaseRecorder()
    {
        lock (releaseSync)
        {
            if (recorderRelease is not null)
            {
                return recorderRelease;
            }

            Interlocked.Exchange(ref releaseRequested, 1);
            recorder.DataAvailable -= OnDataAvailable;
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            recorderRelease = released.Task;
            _ = Task.Run(() => ReleaseRecorderCoreAsync(released), CancellationToken.None);

            return recorderRelease;
        }
    }

    private async Task ReleaseRecorderCoreAsync(TaskCompletionSource released)
    {
        try
        {
            try
            {
                if (sync.TryEnter())
                {
                    try
                    {
                        recorder.StopRecording();
                    }
                    finally
                    {
                        sync.Exit();
                    }
                }
                else
                {
                    await Task.Run(() =>
                    {
                        lock (sync)
                        {
                            recorder.StopRecording();
                        }
                    }, CancellationToken.None);
                }
            }
            finally
            {
                await recorder.DisposeAsync();
            }

            released.TrySetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            released.TrySetException(exception);
        }
    }

    public void FinishRecognition(bool cancel)
    {
        if (!started)
        {
            completion.TrySetResult();
        }
        else if (cancel)
        {
            recognizer.RecognizeAsyncCancel();
        }
        else
        {
            recognizer.RecognizeAsyncStop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseRecorder();
            FinishRecognition(cancel: true);
#pragma warning disable VSTHRD003 // Native recognition completion is event-driven, not UI-bound.
            await Completion.WaitAsync(TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD003
        }
        catch (TimeoutException)
        {
            // The recorder is already released; a broken recognizer cannot retain the microphone.
        }
        finally
        {
            recorder.RecordingStopped -= OnRecordingStopped;
            recognizer.SpeechDetected -= OnSpeechDetected;
            recognizer.SpeechRecognized -= OnSpeechRecognized;
            recognizer.SpeechRecognitionRejected -= OnSpeechRejected;
            recognizer.RecognizeCompleted -= OnCompleted;
            recognizer.Dispose();
            endpoint?.Dispose();
        }
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long position, long timestamp) =>
        DataAvailable?.Invoke(this, buffer);

    private void OnSpeechDetected(object? sender, SpeechDetectedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, recognizer))
        {
            SpeechDetected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, recognizer))
        {
            TranscriptRecognized?.Invoke(this,
                new VoiceTranscriptEventArgs(eventArgs.Result.Text, eventArgs.Result.Confidence));
        }
    }

    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, recognizer))
        {
            RecognitionFailed?.Invoke(this,
                new VoiceRecognitionFailureEventArgs("Speech did not match a supported command."));
        }
    }

    private void OnCompleted(object? sender, RecognizeCompletedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, recognizer))
        {
            if (eventArgs.Error is not null)
            {
                completion.TrySetException(eventArgs.Error);
            }
            else
            {
                completion.TrySetResult();
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, recorder) && eventArgs.Exception is not null)
        {
            RecognitionFailed?.Invoke(this, new VoiceRecognitionFailureEventArgs("Microphone capture failed."));
        }
    }
}
