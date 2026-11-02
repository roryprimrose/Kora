using System.Diagnostics;

using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal sealed class WindowsActivatedCapture : IActivatedCapture, IActivatedHypothesisSource
{
    private readonly Lock sync = new();
    private readonly Lock releaseSync = new();
    private readonly Lock callbackGate = new();
    private readonly WindowsRecognizerResidency.Lease lease;
    private readonly IWindowsActivatedRecognizer recognizer;
    private readonly IWindowsActivatedMicrophone? recorder;
    private readonly BlockingAudioStream stream;
    private readonly Action requireEligible;
    private readonly Func<bool> canStart;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? recorderRelease;
    private Task? disposal;
    private bool started;
    private int releaseRequested;
    private int callbacksRetired;
    private long recorderReleasedTimestamp;

    public WindowsActivatedCapture(WindowsRecognizerResidency.Lease lease, IWindowsActivatedMicrophone? recorder,
        BlockingAudioStream stream, Action requireEligible, Func<bool> canStart)
    {
        this.lease = lease;
        recognizer = lease.Recognizer;
        this.recorder = recorder;
        this.stream = stream;
        this.requireEligible = requireEligible;
        this.canStart = canStart;
        recognizer.SpeechDetected += OnSpeechDetected;
        recognizer.TranscriptRecognized += OnSpeechRecognized;
        recognizer.Hypothesized += OnSpeechHypothesized;
        recognizer.RecognitionFailed += OnSpeechRejected;
        recognizer.Completed += OnCompleted;
        if (recorder is not null)
        {
            recorder.DataAvailable += OnDataAvailable;
            recorder.RecordingFailed += OnRecordingFailed;
        }
    }

    public event ActivatedAudioAvailable? DataAvailable;
    public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
    public event EventHandler<ActivatedHypothesisEventArgs>? Hypothesized;
    public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;
    public event EventHandler? SpeechDetected;

#pragma warning disable VSTHRD003 // Native callbacks complete this asynchronous receipt.
    public Task Completion => completion.Task;
#pragma warning restore VSTHRD003
    public long? RecorderReleasedTimestamp => Volatile.Read(ref recorderReleasedTimestamp) is var timestamp
        && timestamp != 0 ? timestamp : null;

    public void Start()
    {
        if (!canStart()) { throw new OperationCanceledException("The native capture generation is no longer current."); }
        requireEligible();
        lock (sync)
        {
            if (Volatile.Read(ref releaseRequested) != 0)
            {
                throw new InvalidOperationException("Capture was invalidated before recording began.");
            }
            started = true;
            try
            {
                recognizer.Start();
                if (Volatile.Read(ref releaseRequested) != 0 || !canStart())
                { throw new OperationCanceledException("Capture was retired during native start."); }
                recorder?.Start();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lease.Quarantine(exception);
                throw;
            }
        }
    }

    public Task ReleaseRecorder()
    {
        Interlocked.Exchange(ref releaseRequested, 1);
        lock (releaseSync)
        {
            return recorderRelease ??= Task.Run(ReleaseRecorderCoreAsync, CancellationToken.None);
        }
    }

    private async Task ReleaseRecorderCoreAsync()
    {
        // Join a synchronous native start before disposing its recorder, without blocking invalidation.
        lock (sync) { Interlocked.Exchange(ref releaseRequested, 1); }
        if (recorder is not null)
        {
            recorder.DataAvailable -= OnDataAvailable;
            recorder.RecordingFailed -= OnRecordingFailed;
            await recorder.DisposeAsync().ConfigureAwait(false);
        }
        Volatile.Write(ref recorderReleasedTimestamp, Stopwatch.GetTimestamp());
    }

    public void FinishRecognition(bool cancel)
    {
        lock (sync)
        {
            if (!started) { completion.TrySetResult(); }
            else if (!completion.Task.IsCompleted)
            {
                try { recognizer.Finish(cancel); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    lease.Quarantine(exception);
                    throw;
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (releaseSync) { return new(disposal ??= Task.Run(DisposeCoreAsync, CancellationToken.None)); }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref callbacksRetired, 1);
        stream.ClearAndComplete();
        try
        {
            await ReleaseRecorder().ConfigureAwait(false);
            FinishRecognition(cancel: true);
#pragma warning disable VSTHRD003 // A missing native completion retains ownership; a timeout cannot authorize reuse.
            await Completion.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            recognizer.SpeechDetected -= OnSpeechDetected;
            recognizer.TranscriptRecognized -= OnSpeechRecognized;
            recognizer.Hypothesized -= OnSpeechHypothesized;
            recognizer.RecognitionFailed -= OnSpeechRejected;
            recognizer.Completed -= OnCompleted;
            lock (callbackGate) { Interlocked.Exchange(ref callbacksRetired, 1); }
            await recognizer.ResetAsync().ConfigureAwait(false);
            stream.ClearAndComplete();
            await stream.DisposeAsync().ConfigureAwait(false);
            await lease.ReturnAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lease.Quarantine(exception);
            throw;
        }
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer)
    {
        lock (callbackGate)
        {
            if (Volatile.Read(ref callbacksRetired) == 0 && Volatile.Read(ref releaseRequested) == 0)
            { DataAvailable?.Invoke(this, buffer); }
        }
    }

    private void OnSpeechDetected(object? sender, EventArgs args)
    {
        lock (callbackGate)
        {
            if (ReferenceEquals(sender, recognizer) && Volatile.Read(ref callbacksRetired) == 0)
            { SpeechDetected?.Invoke(this, EventArgs.Empty); }
        }
    }

    private void OnSpeechRecognized(object? sender, VoiceTranscriptEventArgs args)
    {
        lock (callbackGate)
        {
            if (ReferenceEquals(sender, recognizer) && Volatile.Read(ref callbacksRetired) == 0)
            { TranscriptRecognized?.Invoke(this, args); }
        }
    }

    private void OnSpeechHypothesized(object? sender, ActivatedHypothesisEventArgs args)
    {
        lock (callbackGate)
        {
            if (ReferenceEquals(sender, recognizer) && Volatile.Read(ref callbacksRetired) == 0
                && Volatile.Read(ref releaseRequested) == 0)
            { Hypothesized?.Invoke(this, args); }
        }
    }

    private void OnSpeechRejected(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        lock (callbackGate)
        {
            if (ReferenceEquals(sender, recognizer) && Volatile.Read(ref callbacksRetired) == 0)
            { RecognitionFailed?.Invoke(this, args); }
        }
    }

    private void OnCompleted(object? sender, WindowsRecognitionCompletedEventArgs args)
    {
        lock (callbackGate)
        {
            if (!ReferenceEquals(sender, recognizer)) { return; }
            if (args.Error is not null) { completion.TrySetException(args.Error); }
            else { completion.TrySetResult(); }
        }
    }

    private void OnRecordingFailed(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        lock (callbackGate)
        {
            if (Volatile.Read(ref callbacksRetired) == 0) { RecognitionFailed?.Invoke(this, args); }
        }
    }
}
