using Kora.Core.Voice;

namespace Kora.Application.Voice;

internal sealed class WakeSpeechInterruptionCapture : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<bool> admitted;
    private readonly Action<WakeSpeechInterruptionCapture> released;
    private readonly Action captureChanged;
    private readonly TimeProvider timeProvider;
    private readonly Task worker;
    private int retired;

    internal WakeSpeechInterruptionCapture(IHandsFreeAudioStage stage, HandsFreeAudioBinding binding,
        Func<bool> admitted, Func<VoiceTranscriptEventArgs, Func<bool>, Task> dispatch,
        Action<WakeSpeechInterruptionCapture> released, Action captureChanged,
        TimeProvider timeProvider, CancellationToken token)
    {
        this.admitted = admitted;
        this.released = released;
        this.captureChanged = captureChanged;
        this.timeProvider = timeProvider;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        worker = Task.Run(() => RunAsync(stage, binding, dispatch), CancellationToken.None);
    }

    internal bool IsQuiescent => closed.Task.IsCompletedSuccessfully;
#pragma warning disable VSTHRD003 // The host joins this owner's exact physical capture receipt.
    internal Task CaptureClosed => closed.Task;
#pragma warning restore VSTHRD003
    internal bool IsCurrent
    {
        get
        {
            if (Volatile.Read(ref retired) != 0) { return false; }
            if (cancellation.IsCancellationRequested || !admitted()) { Invalidate(); return false; }
            return Volatile.Read(ref retired) == 0;
        }
    }

    internal void Revalidate()
    {
        if (!IsCurrent) { Invalidate(); }
    }

    internal void Invalidate()
    {
        if (Interlocked.Exchange(ref retired, 1) == 0) { cancellation.Cancel(); }
    }

    private void RequireCurrent()
    {
        if (!IsCurrent) { throw new OperationCanceledException("The original wake/playback admission retired."); }
    }

    private async Task RunAsync(IHandsFreeAudioStage stage, HandsFreeAudioBinding binding,
        Func<VoiceTranscriptEventArgs, Func<bool>, Task> dispatch)
    {
        IHandsFreeAudioSession? session = null;
        try
        {
            VoiceTranscriptEventArgs? transcript;
            try
            {
                RequireCurrent();
                session = await stage.OpenSpeechInterruptionAsync(binding with { CanCapture = () => IsCurrent }, cancellation.Token);
                RequireCurrent();
                var wake = await session.WaitForWakeAsync(cancellation.Token);
                RequireCurrent();
                if (wake.Generation != binding.Generation || wake.CommandStartSample < 0 || !wake.PlaybackExcluded)
                { throw new InvalidOperationException("Playback exclusion or the exact wake generation was not confirmed."); }
                using var deadline = new CancellationTokenSource(binding.Timing.MaximumCapture, timeProvider);
                using var command = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, deadline.Token);
                try { transcript = await session.CaptureCommandAsync(wake, command.Token); }
                catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
                { throw new TimeoutException("The bounded wake interruption command expired.", exception); }
                RequireCurrent();
                if (transcript is not null && (transcript.Generation != binding.Generation
                    || transcript.ActivationEvidence is not null || !float.IsFinite(transcript.Confidence)
                    || transcript.Confidence is < 0.62f or > 1 || string.IsNullOrWhiteSpace(transcript.Transcript)
                    || transcript.Transcript.Length > VoiceInputLimits.MaximumTranscriptCharacters))
                { throw new InvalidOperationException("The original playback interruption transcript was invalid."); }
            }
            finally
            {
                await CloseAsync(session);
            }
            RequireCurrent();
            if (transcript is not null) { await dispatch(transcript, () => IsQuiescent && IsCurrent); }
        }
        catch (OperationCanceledException) when (IsQuiescent && !IsCurrent)
        {
            // Retirement is not a release receipt; the finally above must have joined native capture.
        }
    }

    private async Task CloseAsync(IHandsFreeAudioSession? session)
    {
        try
        {
            if (session is not null)
            {
                session.Invalidate();
                await session.DisposeAsync();
                if (!session.IsQuiescent) { throw new InvalidOperationException("Wake interruption capture release is unconfirmed."); }
            }
            closed.TrySetResult();
            captureChanged();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            closed.TrySetException(exception);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Invalidate();
        try
        {
#pragma warning disable VSTHRD003 // Joins this owner's native capture and single exact-playback callback.
            await worker;
#pragma warning restore VSTHRD003
        }
        finally
        {
            if (IsQuiescent)
            {
                released(this);
                cancellation.Dispose();
            }
        }
    }
}
