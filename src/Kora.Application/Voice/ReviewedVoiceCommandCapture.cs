using System.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

/// <summary>One opt-in command capture, inert editable review and separate original-voice dispatch.</summary>
public sealed partial class ReviewedVoiceCommandCapture(
    IActivatedVoiceRecognitionService capture,
    IUiDispatcher dispatcher,
    Func<ReviewedVoiceCommandTarget?> observe,
    ILogger<ReviewedVoiceCommandCapture> logger,
    TimeProvider time) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private Turn? turn;
    private Task retirement = Task.CompletedTask;
    private long nextRevision;
    private bool closed;
    private string status = "Review one spoken command before dispatch. Direct push-to-talk is unchanged.";

    public event EventHandler? Changed;
    public bool IsActive { get { lock (gate) { return turn is not null; } } }
    public bool IsCaptureReleased { get { lock (gate) { return turn is null && retirement.IsCompletedSuccessfully; } } }
    public bool HasReview { get { lock (gate) { return turn?.Review == true; } } }
    public string Candidate { get { lock (gate) { return turn?.Review == true ? turn.Text! : string.Empty; } } }
    public string Status { get { lock (gate) { return status; } } }
    public long Revision { get { lock (gate) { return turn?.Revision ?? 0; } } }
#pragma warning disable VSTHRD003 // The capture owner exposes its joined retirement, never a blocking wait.
    public Task Quiescence { get { lock (gate) { return retirement; } } }
#pragma warning restore VSTHRD003

    public async Task BeginAsync(Func<bool> nativeLifetime)
    {
        var target = observe();
        Turn current;
        lock (gate)
        {
            if (closed || turn is not null || !retirement.IsCompletedSuccessfully
                || !capture.IsCaptureQuiescent || target is null || !target.IsCurrent() || !nativeLifetime())
            {
                status = "Reviewed command unavailable: current consented command readiness, exclusive input and an exact private target are required. No capture opened.";
                Publish();
                return;
            }
            current = new(target, nativeLifetime, time, _ => Retire("Reviewed command expired. Candidate cleared; no dispatch."));
            turn = current;
            status = "Capturing ONE local command for review only. Choose Finish capture, then correct and separately dispatch, or Cancel.";
            capture.TranscriptRecognized += Transcript;
            capture.RecognitionFailed += RecognitionFailed;
            capture.RecognitionCompleted += Completed;
            capture.CaptureStateChanged += CaptureChanged;
        }
        Publish();
        using var activity = BeginActivity(current, HostOperation.Presentation);
        try
        {
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(current.Cancellation.Token);
            opening.CancelAfter(TimeSpan.FromSeconds(5));
            if (!Eligible(current)) { throw new OperationCanceledException(); }
            await capture.BeginPushToTalkAsync(target.Microphone, [], target.AssistantName, opening.Token);
            lock (gate) { current.Generation = capture.CaptureGeneration; }
            if (!Eligible(current) || !capture.AcceptCaptureGeneration(current.Generation))
            { throw new OperationCanceledException(); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            Retire("Reviewed command capture failed or was cancelled. No command dispatched; microphone release required.");
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally { current.Opened.TrySetResult(); Publish(); }
    }

    public async Task FinishAsync()
    {
        Turn current;
        lock (gate)
        {
            if (turn is not { Review: false, Finishing: false } observed) { return; }
            current = observed;
            if (!current.Opened.Task.IsCompleted)
            {
                Retire("Capture released before opening completed. No command admitted.");
                return;
            }
            current.Finishing = true;
            current.Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        using var activity = BeginActivity(current, HostOperation.Presentation);
        try
        {
            if (!Eligible(current)) { throw new OperationCanceledException(); }
            await capture.EndPushToTalkAsync(current.Cancellation.Token);
            if (!Eligible(current) || capture.CaptureGeneration != current.Generation) { throw new OperationCanceledException(); }
            await capture.StopAsync();
            if (!capture.IsCaptureQuiescent) { throw new InvalidOperationException("Capture release is unconfirmed."); }
            lock (gate) { current.ReleasedGeneration = capture.CaptureGeneration; }
            await dispatcher.InvokeAsync(() =>
            {
                lock (gate)
                {
                    if (!Eligible(current) || current.Text is null) { throw new OperationCanceledException(); }
                    current.Review = true;
                    current.Revision = checked(++nextRevision);
                    status = "Microphone closed. Exact final transcript below is inert. Editing does not execute. Dispatch remains VOICE-origin and is not operation approval.";
                }
                Publish();
                return Task.CompletedTask;
            });
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            Retire("No current complete transcript was admitted, or microphone release failed. Candidate cleared; no dispatch.");
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally { current.Finished.TrySetResult(); Publish(); }
    }

    public bool Edit(long revision, string text)
    {
        lock (gate)
        {
            if (turn is not { Review: true } current || revision != current.Revision || !Eligible(current))
            {
                status = "Transcript edit expired. No text or target was changed and no command dispatched.";
                Revalidate();
                Publish();
                return false;
            }
            if (!Validate(text)) { return false; }
            current.Text = text;
            current.Revision = checked(++nextRevision);
        }
        Publish();
        return true;
    }

    public async Task ConfirmAsync(long revision)
    {
        Turn current;
        string text;
        lock (gate)
        {
            if (turn is not { Review: true } observed || observed.Revision != revision || !Eligible(observed))
            {
                status = "Transcript confirmation expired. No command dispatched.";
                Revalidate();
                Publish();
                return;
            }
            current = observed;
            text = current.Text!;
            current.Review = false;
            current.Dispatching = true;
            current.Text = null;
            status = "Candidate consumed once. Applying normal VOICE-origin command admission, not approving its operation.";
        }
        Publish();
        using var activity = BeginActivity(current, HostOperation.Request);
        try
        {
            await current.Deadline.DisposeAsync();
            if (!Eligible(current)) { throw new OperationCanceledException(); }
            await current.Target.Dispatch(new(text, 1, current.ReleasedGeneration!.Value), current.Request,
                () => Eligible(current));
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            status = "Reviewed command dispatch failed or admission expired. No replay was queued; inspect the normal operation outcome.";
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally { Retire(status); }
    }

    private bool Validate(string text)
    {
        try { ConversationText.Validate(text, submission: true); return true; }
        catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
        {
            Retire("Transcript rejected by existing text/credential limits. Candidate cleared, never truncated or dispatched.");
            return false;
        }
    }

    private bool Eligible(Turn current)
    {
        lock (gate)
        {
            return !closed && ReferenceEquals(turn, current) && !current.Cancellation.IsCancellationRequested
                && current.NativeLifetime() && current.Target.IsCurrent()
                && (current.Generation < 0 || current.Finishing && current.ReleasedGeneration is null
                    || capture.CaptureGeneration == (current.ReleasedGeneration ?? current.Generation));
        }
    }

    public void Revalidate()
    {
        lock (gate)
        {
            if (turn is { Dispatching: false } current && !Eligible(current))
            { Retire("Original command source, target or host admission changed. Candidate cleared; no retargeting or dispatch."); }
        }
    }

    private void Transcript(object? sender, VoiceTranscriptEventArgs args)
    {
        lock (gate)
        {
            if (turn is not { Review: false, Text: null } current || args.Generation != current.Generation
                || args.Generation != capture.CaptureGeneration || !Eligible(current)) { return; }
            if (!Validate(args.Transcript)) { return; }
            current.Text = args.Transcript;
        }
    }

    private void RecognitionFailed(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        lock (gate)
        {
            if (turn?.Generation == args.Generation) { Retire("Local recognition failed. Candidate cleared; no dispatch."); }
        }

    }

    private void CaptureChanged(object? sender, VoiceCaptureStateChangedEventArgs args) => Revalidate();

    private void Completed(object? sender, VoiceRecognitionCompletedEventArgs args)
    {
        lock (gate)
        {
            if (turn?.Generation == args.Generation && args.Reason is not
                (VoiceRecognitionCompletionReason.Recognized or VoiceRecognitionCompletionReason.PushToTalkReleased))
            { Retire("Capture ended without an admitted final result. Candidate cleared; no dispatch."); }
        }
    }

    public void Retire(string reason)
    {
        Turn? previous;
        lock (gate)
        {
            previous = turn;
            turn = null;
            status = reason;
            if (previous is null) { return; }
            previous.Text = null;
            capture.TranscriptRecognized -= Transcript;
            capture.RecognitionFailed -= RecognitionFailed;
            capture.RecognitionCompleted -= Completed;
            capture.CaptureStateChanged -= CaptureChanged;
            previous.Cancellation.Cancel();
            if (!previous.Dispatching) { capture.InvalidateCapture(); }
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            retirement = released.Task;
            _ = CompleteReleaseAsync(previous, released);
        }
        Publish();
    }

    private async Task CompleteReleaseAsync(Turn previous, TaskCompletionSource released)
    {
        try
        {
            await ReleaseAsync(previous).ConfigureAwait(false);
            released.TrySetResult();
        }
        catch (Exception exception) { released.TrySetException(exception); }
        finally { Publish(); }
    }

    private async Task ReleaseAsync(Turn previous)
    {
        try
        {
#pragma warning disable VSTHRD003 // Join this exact open/end before releasing the shared capture slot.
            await previous.Opened.Task.ConfigureAwait(false);
            if (!previous.Dispatching) { await capture.StopAsync().ConfigureAwait(false); }
            await previous.Finished.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (!previous.Dispatching && !capture.IsCaptureQuiescent) { throw new InvalidOperationException("Capture release is unconfirmed."); }
        }
        catch (Exception exception)
        {
            Failed(logger, exception.GetType().Name);
            lock (gate) { status = "Microphone release unconfirmed. Restart Kora before capturing again."; }
            Publish();
            throw;
        }
        finally
        {
            await previous.Deadline.DisposeAsync().ConfigureAwait(false);
            previous.Cancellation.Dispose();
        }
    }

    private static HostActivity BeginActivity(Turn current, HostOperation operation)
    {
        var activity = HostActivity.BeginRoot(current.Request, HostActivityLayer.Application, operation,
            current.Context == default ? [] : [new ActivityLink(current.Context)]);
        current.Context = activity.Activity?.Context ?? current.Context;
        return activity;
    }
    private void Publish() => dispatcher.Post(() => Changed?.Invoke(this, EventArgs.Empty));

    public void CloseAdmission()
    {
        lock (gate)
        {
            closed = true;
            Retire("Reviewed command host closed. Candidate cleared; no dispatch.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        CloseAdmission();
#pragma warning disable VSTHRD003 // Disposal joins all owned capture resources.
        await Quiescence.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private sealed class Turn(ReviewedVoiceCommandTarget target, Func<bool> nativeLifetime, TimeProvider time, TimerCallback expired)
    {
        public ReviewedVoiceCommandTarget Target { get; } = target;
        public Func<bool> NativeLifetime { get; } = nativeLifetime;
        public HostRequest Request { get; } = HostRequest.Create(RequestOrigin.ActivatedVoice);
        public ActivityContext Context { get; set; } = Activity.Current?.Context ?? default;
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; set; } = CompletedSource();
        public ITimer Deadline { get; } = time.CreateTimer(expired, null, VoiceInputLimits.MaximumCommandDuration, Timeout.InfiniteTimeSpan);
        public long Generation { get; set; } = -1;
        public long? ReleasedGeneration { get; set; }
        public string? Text { get; set; }
        public long Revision { get; set; }
        public bool Review { get; set; }
        public bool Finishing { get; set; }
        public bool Dispatching { get; set; }
        private static TaskCompletionSource CompletedSource()
        {
            var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            result.SetResult();
            return result;
        }
    }
}
