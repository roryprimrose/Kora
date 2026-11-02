using System.Runtime.InteropServices;

using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal sealed class WindowsHandsFreeAudioSession : IHandsFreeAudioSession
{
    private readonly HandsFreeAudioBinding binding;
    private readonly IHandsFreeMicrophone microphone;
    private readonly IHandsFreeKeywordDetector detector;
    private readonly IHandsFreeCommandEndpoint endpoint;
    private readonly IAsyncDisposable assets;
    private readonly Func<BlockingAudioStream, Task<IActivatedCapture>> openCommand;
    private readonly HandsFreeSetupRuntime.CaptureOwnership? ownership;
    private readonly Lock processing = new();
    private readonly Lock lifetime = new();
    private readonly WakeAudioBuffer ring = new(16000);
    private readonly TaskCompletionSource<WakeWordObservation> wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BlockingAudioStream commandAudio = new();
    private IActivatedCapture? command;
    private Task<IActivatedCapture>? opening;
    private VoiceTranscriptEventArgs? result;
    private WakeWordObservation? observation;
    private Exception? failure;
    private Task? release;
    private int invalidated;
    private int commandSamples;
    private int captureStarted;
    private bool feeding;
    private bool ended;
    private bool quiescent;

    public WindowsHandsFreeAudioSession(HandsFreeAudioBinding binding, IHandsFreeMicrophone microphone,
        IHandsFreeKeywordDetector detector, IHandsFreeCommandEndpoint endpoint, IAsyncDisposable assets,
        Func<BlockingAudioStream, Task<IActivatedCapture>> openCommand,
        HandsFreeSetupRuntime.CaptureOwnership? ownership = null)
    {
        this.binding = binding;
        this.microphone = microphone;
        this.detector = detector;
        this.endpoint = endpoint;
        this.assets = assets;
        this.openCommand = openCommand;
        this.ownership = ownership;
        microphone.Samples += OnSamples;
        microphone.Failed += OnMicrophoneFailed;
    }

    public bool IsQuiescent => Volatile.Read(ref quiescent);

    public void Start()
    {
        RequireCurrent();
        microphone.Start();
        RequireCurrent();
    }

    public async Task<WakeWordObservation> WaitForWakeAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(Invalidate);
        return await wake.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<VoiceTranscriptEventArgs?> CaptureCommandAsync(
        WakeWordObservation wakeObservation, CancellationToken cancellationToken)
    {
        if (wakeObservation != observation || !wakeObservation.PlaybackExcluded)
        {
            throw new InvalidOperationException("Command audio requires this exact native wake span.");
        }
        if (Interlocked.Exchange(ref captureStarted, 1) != 0)
        {
            throw new InvalidOperationException("The native wake interval can be consumed only once.");
        }
        using var registration = cancellationToken.Register(Invalidate);
        RequireCurrent();
        // The owning host waits for this open even if the native constructor ignores cancellation.
        Task<IActivatedCapture> pending;
        lock (lifetime)
        {
            RequireCurrent();
            pending = opening = openCommand(commandAudio);
        }
        var opened = await pending.ConfigureAwait(false);
        lock (lifetime)
        {
            command = opened;
            RequireCurrent();
            opened.TranscriptRecognized += OnTranscript;
        }
        lock (processing)
        {
            RequireCurrent();
            var buffered = ring.TakeCommand(wakeObservation.CommandStartSample);
            try
            {
                opened.Start();
                feeding = true;
                Feed(buffered);
            }
            finally { Array.Clear(buffered); }
        }
        await Task.WhenAny(opened.Completion, failed.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (failure is not null) { throw new InvalidOperationException("Hands-free command capture failed.", failure); }
        await opened.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        RequireCurrent();
        return result;
    }

    private void OnSamples(ReadOnlySpan<short> samples)
    {
        if (Volatile.Read(ref invalidated) != 0) { return; }
        try
        {
            lock (processing)
            {
                RequireCurrent();
                if (feeding) { Feed(samples); return; }
                ring.Append(samples);
                RequireCurrent();
                if (observation is not null) { return; }
                var span = detector.Accept(samples);
                RequireCurrent();
                if (span is not { } detected) { return; }
                var boundary = detected.CommandStartSample;
                if (boundary > ring.EndSample || boundary < ring.EndSample - ring.Count)
                {
                    throw new InvalidDataException("The native name end is outside the current original-PCM interval.");
                }
                observation = new(binding.Generation, boundary, PlaybackExcluded: true);
                binding.WakeActivated(observation);
                RequireCurrent();
                wake.TrySetResult(observation);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception);
        }
    }

    private void Feed(ReadOnlySpan<short> samples)
    {
        RequireCurrent();
        if (ended) { return; }
        commandSamples = checked(commandSamples + samples.Length);
        if (commandSamples > 16000 * VoiceInputLimits.MaximumCommandDuration.TotalSeconds
            || !commandAudio.TryAdd(MemoryMarshal.AsBytes(samples)))
        {
            throw new InvalidOperationException("The bounded command-only PCM stream overflowed.");
        }
        if (endpoint.Accept(samples))
        {
            ended = true;
            commandAudio.Complete();
            command!.FinishRecognition(cancel: false);
        }
    }

    private void OnTranscript(object? sender, VoiceTranscriptEventArgs transcript)
    {
        if (Volatile.Read(ref invalidated) != 0 || !binding.CanCapture()) { return; }
        if (ReferenceEquals(sender, command) && transcript.Confidence >= 0.62f
            && float.IsFinite(transcript.Confidence) && transcript.Confidence <= 1
            && !string.IsNullOrWhiteSpace(transcript.Transcript)
            && transcript.Transcript.Length <= VoiceInputLimits.MaximumTranscriptCharacters)
        {
            Interlocked.CompareExchange(ref result,
                new(transcript.Transcript, transcript.Confidence, binding.Generation), null);
        }
    }

    private void OnMicrophoneFailed(object? sender, EventArgs args) =>
        Fail(new InvalidOperationException("The hands-free microphone failed."));

    private void Fail(Exception exception)
    {
        Interlocked.CompareExchange(ref failure, exception, null);
        failed.TrySetResult();
        wake.TrySetException(exception);
        Invalidate();
    }

    private void RequireCurrent()
    {
        if (Volatile.Read(ref invalidated) != 0 || !binding.CanCapture())
        {
            ring.Retire();
            throw new OperationCanceledException("The original hands-free authority was retired.");
        }
    }

    public void Invalidate()
    {
        Interlocked.Exchange(ref invalidated, 1);
        ring.Retire();
        commandAudio.ClearAndComplete();
        wake.TrySetCanceled();
    }

    public ValueTask DisposeAsync()
    {
        lock (lifetime) { return new(release ??= ReleaseOwnedAsync()); }
    }

    private async Task ReleaseOwnedAsync()
    {
        try
        {
            await ReleaseAsync().ConfigureAwait(false);
            if (ownership is not null) { await ownership.DisposeAsync().ConfigureAwait(false); }
        }
        catch
        {
            ownership?.Fail();
            throw;
        }
    }

    private async Task ReleaseAsync()
    {
        Invalidate();
        microphone.Samples -= OnSamples;
        microphone.Failed -= OnMicrophoneFailed;
        try { await microphone.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try
            {
                await Task.Run(() => { lock (processing) { } }, CancellationToken.None).ConfigureAwait(false);
#pragma warning disable VSTHRD003 // The owned native-open task runs on the pool and must join even after authority loss.
                if (opening is not null) { command = await opening.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
                if (command is not null)
                {
                    command.TranscriptRecognized -= OnTranscript;
                    command.FinishRecognition(cancel: true);
                    await command.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await Task.Run(() =>
                    {
                        lock (processing)
                        {
                            try { detector.Dispose(); }
                            finally { endpoint.Dispose(); }
                        }
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    await commandAudio.DisposeAsync().ConfigureAwait(false);
                    await assets.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        Volatile.Write(ref quiescent, true);
    }
}