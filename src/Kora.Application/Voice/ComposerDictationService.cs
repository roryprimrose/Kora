using System.ComponentModel;
using System.Diagnostics;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

/// <summary>One explicitly armed native buffer, one inert utterance, and confirmed capture release before publication.</summary>
public sealed partial class ComposerDictationService : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly IActivatedVoiceRecognitionService capture;
    private readonly IUiDispatcher dispatcher;
    private readonly Func<ComposerDictationAdmission?> observe;
    private readonly ILogger<ComposerDictationService> logger;
    private readonly TimeProvider time;
    private Turn? turn;
    private Task retirement = Task.CompletedTask;
    private bool closed;
    private string status = "Microphone closed. Hold Dictate explicitly; literal dictation requires qualified capture and current consented PTT readiness.";

    public ComposerDictationService(IActivatedVoiceRecognitionService capture, IUiDispatcher dispatcher,
        Func<ComposerDictationAdmission?> observe, ILogger<ComposerDictationService> logger, TimeProvider? time = null)
    {
        this.capture = capture;
        this.dispatcher = dispatcher;
        this.observe = observe;
        this.logger = logger;
        this.time = time ?? TimeProvider.System;
        if (!capture.IsLiteralDictationAvailable)
        {
            status = "Microphone closed. Literal dictation unavailable: this capture/transcriber is not qualified. No consent, device or listening change was made.";
        }
        capture.TranscriptRecognized += Transcript;
        capture.RecognitionFailed += Failed;
        capture.RecognitionCompleted += Completed;
    }

    public event EventHandler? Changed;
    public string Status { get { lock (gate) { return status; } } }
    public bool IsActive { get { lock (gate) { return turn is not null; } } }
    public bool HasReview { get { lock (gate) { return turn?.Review == true; } } }
    public string OriginalBody { get { lock (gate) { return turn?.Review == true ? turn.Original : string.Empty; } } }
    public string LiteralText { get { lock (gate) { return turn?.Review == true ? turn.Literal! : string.Empty; } } }
#pragma warning disable VSTHRD003 // The service owns this asynchronous native-release receipt; never block the dispatcher.
    public Task Quiescence { get { lock (gate) { return retirement; } } }
#pragma warning restore VSTHRD003

    public async Task BeginAsync(NewRequestComposer composer, Func<bool> exactNativeArm, CancellationToken token)
    {
        var admission = observe();
        Turn? opened = null;
        lock (gate)
        {
            if (!closed && turn is null && retirement.IsCompletedSuccessfully && capture.IsCaptureQuiescent
                && capture.IsLiteralDictationAvailable && admission is not null && exactNativeArm()
                && composer.AdmitsDictation(composer.DictationRevision, composer.DictationControlRevision)
                && !token.IsCancellationRequested)
            {
                opened = new(composer, admission, exactNativeArm, token);
                turn = opened;
                composer.PropertyChanged += BufferChanged;
                status = "Opening explicitly armed literal dictation. No command or question routing.";
            }
            else { status = "Dictation unavailable: qualified literal transcription, consented PTT readiness, released capture and an exact native buffer are required. Nothing changed."; }
        }
        Publish();
        if (opened is null) { return; }
        using var activity = BeginActivity(opened, HostOperation.Presentation);
        opened.Registration = token.Register(() => Retire("Dictation cancelled. Original memory preserved."));
        try
        {
            using var deadline = time.CreateTimer(_ => Retire("Dictation opening timed out. Microphone release required; original memory preserved."),
                null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            if (!Eligible(opened)) { throw new OperationCanceledException("Dictation arm retired before capture."); }
            await capture.BeginPushToTalkAsync(admission!.Microphone, [], cancellationToken: opened.Cancellation.Token);
            lock (gate) { opened.Generation = capture.CaptureGeneration; }
            if (!Eligible(opened) || !capture.AcceptCaptureGeneration(opened.Generation))
            { throw new OperationCanceledException("Dictation generation acknowledgement retired."); }
            lock (gate)
            {
                opened.Context = activity.Activity!.Context;
                status = "Capturing literal draft text only. Release Dictate to finish; Cancel discards it.";
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            Retire("Dictation unavailable or cancelled. Original memory preserved; microphone release is pending.");
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally { opened.Opened.TrySetResult(); Publish(); }
    }

    public async Task EndAsync()
    {
        Turn? current;
        bool opening;
        lock (gate)
        {
            current = turn;
            if (current is null || current.Releasing || current.Review) { return; }
            opening = !current.Opened.Task.IsCompleted;
            if (!opening)
            {
                current.Releasing = true;
                current.Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        if (opening)
        {
            Retire("Dictate released before opening completed. No literal result admitted; native release required.");
#pragma warning disable VSTHRD003 // Early button release joins the cancelled native open and its cleanup.
            await Quiescence;
#pragma warning restore VSTHRD003
            return;
        }
        using var activity = BeginActivity(current, HostOperation.Request);
        try
        {
#pragma warning disable VSTHRD003 // Release owns the exact open receipt, not a new activation.
            await current.Opened.Task;
#pragma warning restore VSTHRD003
            if (!Eligible(current)) { throw new OperationCanceledException("Dictation target retired before release."); }
            await capture.EndPushToTalkAsync();
            if (capture.CaptureGeneration != current.Generation)
            { throw new OperationCanceledException("Dictation capture generation changed."); }
            await capture.StopAsync();
            if (!capture.IsCaptureQuiescent) { throw new InvalidOperationException("Native capture release is unconfirmed."); }
            await dispatcher.InvokeAsync(() =>
            {
                if (!Eligible(current)) { throw new OperationCanceledException("Dictation target retired before publication."); }
                if (current.Literal is null) { Retire("No literal text was heard. Original memory preserved; microphone closed."); }
                else if (current.Original.Length == 0) { Apply(current, current.Literal); }
                else
                {
                    lock (gate)
                    {
                        current.Review = true;
                        status = "Microphone released. Review complete original and literal text, then explicitly Append, Replace or Cancel. Nothing has been inserted.";
                    }
                    Publish();
                }
                return Task.CompletedTask;
            });
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            Retire("Dictation failed or became stale. Original memory preserved; confirm microphone release before retrying.");
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally { current.Ended.TrySetResult(); }
    }

    public void Append() => Decide(append: true);
    public void Replace() => Decide(append: false);

    private void Decide(bool append)
    {
        Turn? current;
        lock (gate) { current = turn; }
        if (current is null || !current.Review) { return; }
        Apply(current, append ? current.Original + current.Literal : current.Literal!);
    }

    private void Apply(Turn current, string literal)
    {
        if (!Eligible(current)) { Retire("Dictation review retired. Original memory preserved."); return; }
        // Do not let binding callbacks revalidate the consumed revision, but retain the native/host admission fence.
        current.Composer.PropertyChanged -= BufferChanged;
        var inserted = current.Composer.InsertDictation(current.Revision, current.Control, literal,
            () => HostEligible(current));
        Retire(inserted ? "Literal text inserted into private draft memory only. No Create, Save, Submit, answer or execution."
            : "Dictation insertion revoked. Original or newer independent memory preserved.");
    }

    private bool HostEligible(Turn current) =>
        !closed && !current.Cancellation.IsCancellationRequested && capture.IsLiteralDictationAvailable
        && current.NativeArm() && observe() == current.Admission;

    private bool Eligible(Turn current)
    {
        lock (gate)
        {
            return ReferenceEquals(turn, current) && HostEligible(current)
                && current.Composer.AdmitsDictation(current.Revision, current.Control);
        }
    }

    public void Revalidate()
    {
        Turn? current;
        lock (gate) { current = turn; }
        if (current is not null && !Eligible(current)) { Retire("Dictation source, buffer or private host admission changed. Late results discarded."); }
    }

    private void BufferChanged(object? sender, PropertyChangedEventArgs args) => Revalidate();

    public void Retire(string reason)
    {
        Turn? previous;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            previous = turn;
            turn = null;
            status = reason;
            if (previous is not null) { retirement = released.Task; }
        }
        if (previous is not null)
        {
            previous.Composer.PropertyChanged -= BufferChanged;
            previous.Literal = null;
            capture.InvalidateCapture();
            previous.Cancellation.Cancel();
            _ = CompleteReleaseAsync(ReleaseAsync(previous), released);
        }
        Publish();
    }

    private static async Task CompleteReleaseAsync(Task closing, TaskCompletionSource released)
    {
        try
        {
#pragma warning disable VSTHRD003 // Transfer the complete owned native/disposal task outcome to the already published receipt.
            await closing.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            released.TrySetResult();
        }
        catch (Exception exception) { released.TrySetException(exception); }
    }

    private async Task ReleaseAsync(Turn previous)
    {
        using var activity = BeginActivity(previous, HostOperation.Recovery);
        try
        {
#pragma warning disable VSTHRD003 // Native open/release and publication are owned receipts; cancellation cannot skip resource release.
            await previous.Opened.Task.ConfigureAwait(false);
            await capture.StopAsync().ConfigureAwait(false);
            await previous.Ended.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (!capture.IsCaptureQuiescent) { throw new InvalidOperationException("Native capture release is unconfirmed."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            lock (gate) { status = "Dictation microphone cleanup unconfirmed. Restart Kora before trying voice again."; }
            activity.Complete(HostOperationOutcome.Failed);
            Publish();
            throw;
        }
        finally
        {
            await previous.Registration.DisposeAsync().ConfigureAwait(false);
            previous.Cancellation.Dispose();
        }
    }

    private void Transcript(object? sender, VoiceTranscriptEventArgs args)
    {
        lock (gate)
        {
            if (turn is not { } current || current.Review || current.Literal is not null
                || args.Generation != current.Generation || args.Generation != capture.CaptureGeneration || !Eligible(current)) { return; }
            current.Literal = args.Transcript;
        }
    }

    private void Failed(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        lock (gate)
        {
            if (turn is null || args.Generation != turn.Generation) { return; }
        }
        Retire("Literal transcription failed. Original memory preserved; microphone release pending.");
    }

    private void Completed(object? sender, VoiceRecognitionCompletedEventArgs args)
    {
        lock (gate)
        {
            if (turn is null || args.Generation != turn.Generation || turn.Releasing
                || args.Reason is VoiceRecognitionCompletionReason.Recognized or VoiceRecognitionCompletionReason.PushToTalkReleased) { return; }
        }
        Retire("Dictation ended without a released literal result. Original memory preserved.");
    }

    private static HostActivity BeginActivity(Turn current, HostOperation operation) =>
        HostActivity.BeginRoot(current.Request, HostActivityLayer.Application, operation,
            current.Context == default ? [] : [new ActivityLink(current.Context)]);

    private void Publish() => dispatcher.Post(() => Changed?.Invoke(this, EventArgs.Empty));

    public void CloseAdmission()
    {
        closed = true;
        Retire("Dictation closed. Late results discarded; microphone release required.");
        capture.TranscriptRecognized -= Transcript;
        capture.RecognitionFailed -= Failed;
        capture.RecognitionCompleted -= Completed;
    }

    public async ValueTask DisposeAsync()
    {
        CloseAdmission();
#pragma warning disable VSTHRD003 // Disposal joins the owned native release; no synchronous wait or dispatcher blockage.
        await Quiescence.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private sealed class Turn(NewRequestComposer composer, ComposerDictationAdmission admission, Func<bool> nativeArm, CancellationToken token)
    {
        public NewRequestComposer Composer { get; } = composer;
        public long Revision { get; } = composer.DictationRevision;
        public long Control { get; } = composer.DictationControlRevision;
        public string Original { get; } = composer.Body;
        public ComposerDictationAdmission Admission { get; } = admission;
        public Func<bool> NativeArm { get; } = nativeArm;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public CancellationTokenRegistration Registration { get; set; }
        public HostRequest Request { get; } = HostRequest.Create(RequestOrigin.LocalUi);
        public ActivityContext Context { get; set; }
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; set; } = Finished();
        public long Generation { get; set; } = -1;
        public string? Literal { get; set; }
        public bool Releasing { get; set; }
        public bool Review { get; set; }
        private static TaskCompletionSource Finished()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }
    }
}
