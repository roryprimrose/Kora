using System.Diagnostics;
using System.Runtime.ExceptionServices;

using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Definitions;

/// <summary>One explicitly named literal input in one focused private prompt preparation, never a question or command.</summary>
public sealed partial class PromptInputVoiceController : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly IQuestionVoiceRecognitionService capture;
    private readonly IUiDispatcher dispatcher;
    private readonly Func<QuestionVoiceAdmission?> observe;
    private readonly Func<ArtifactDefinition, CancellationToken, Task<bool>> fresh;
    private readonly ILogger<PromptInputVoiceController> logger;
    private readonly TimeProvider time;
    private Turn? turn;
    private Task retirement = Task.CompletedTask;
    private Task delivery = Task.CompletedTask;
    private bool closed;
    private string status = "Microphone closed. Select Speak for one declared input; review and submission remain separate.";

    public PromptInputVoiceController(IQuestionVoiceRecognitionService capture, IUiDispatcher dispatcher,
        Func<QuestionVoiceAdmission?> observe, Func<ArtifactDefinition, CancellationToken, Task<bool>> fresh,
        ILogger<PromptInputVoiceController> logger, TimeProvider time)
    {
        this.capture = capture;
        this.dispatcher = dispatcher;
        this.observe = observe;
        this.fresh = fresh;
        this.logger = logger;
        this.time = time;
        capture.TranscriptRecognized += Transcript;
        capture.RecognitionFailed += Failed;
        capture.RecognitionCompleted += Completed;
    }

    public event EventHandler? Changed;
    public bool IsActive => Volatile.Read(ref turn) is not null;
    public string Status { get { lock (gate) { return status; } } }
#pragma warning disable VSTHRD003 // Exposes only this owner's native release and delivery receipt for asynchronous joining.
    public Task Quiescence { get { lock (gate) { return Task.WhenAll(retirement, delivery); } } }
#pragma warning restore VSTHRD003

    public async Task Begin(PromptParameterDraft draft, string name, Func<bool> exactFocus,
        Action<string, string> apply, CancellationToken token)
    {
        var admission = observe();
        Turn? opened = null;
        lock (gate)
        {
            if (!closed && turn is null && retirement.IsCompletedSuccessfully && delivery.IsCompletedSuccessfully && capture.IsCaptureQuiescent
                && admission is not null && exactFocus() && draft.IsCurrent && !token.IsCancellationRequested
                && draft.RequiredNames.Contains(name, StringComparer.Ordinal))
            {
                draft.RetirePreview();
                opened = new(draft, name, admission, exactFocus, apply, time.GetUtcNow().AddSeconds(75), token);
                turn = opened;
                status = "Revalidating the exact declared input. No question, approval, command or submission is armed.";
            }
            else { status = "Voice input unavailable: select one exact declared name in the focused current prompt and release competing capture."; }
        }
        Publish();
        if (opened is null) { return; }
        using var activity = BeginActivity(opened, HostOperation.Presentation);
        try
        {
            opened.Registration = token.Register(() => Retire(opened, "Prompt input cancelled. Late text discarded."));
            opened.Expiry = time.CreateTimer(_ => Retire(opened, "Prompt input expired. Microphone release required; no text admitted."),
                null, TimeSpan.FromSeconds(75), Timeout.InfiniteTimeSpan);
            opened.Context = HostActivity.RequireCurrent().Activity!.Context;
            if (!Eligible(opened) || !await fresh(draft.Source, opened.Cancellation.Token) || !Eligible(opened))
            { throw new OperationCanceledException("Prompt source or focus changed before capture."); }
            RequireActivity(opened);
            await capture.BeginReplyAsync(admission!.Microphone, [], () => Eligible(opened), opened.Cancellation.Token);
            lock (gate)
            {
                opened.Generation = capture.CaptureGeneration;
            }
            if (!Eligible(opened) || !capture.AcceptCaptureGeneration(opened.Generation))
            { throw new OperationCanceledException("Prompt input activation retired before acknowledgement."); }
            lock (gate)
            {
                if (ReferenceEquals(turn, opened) && !opened.Delivering)
                { status = "Speak one literal value for the selected declared input. Cancel discards it; nothing auto-submits."; }
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            Retire(opened, "Prompt input cancelled or stale. No value admitted; microphone release pending.");
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            Retire(opened, "Prompt voice input failed. Use native text; no automatic retry or fallback.");
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally { opened.Opened.TrySetResult(); Publish(); }
    }

    private bool Eligible(Turn candidate)
    {
        lock (gate)
        {
            return !closed && ReferenceEquals(turn, candidate) && !candidate.Cancellation.IsCancellationRequested
                && candidate.Focus() && candidate.Admission == observe() && time.GetUtcNow() < candidate.ExpiresAt
                && candidate.Draft.IsCurrent && candidate.Draft.InputRevision == candidate.Revision;
        }
    }

    public void Revalidate()
    {
        Turn? current;
        lock (gate) { current = turn; }
        if (current is not null && !Eligible(current)) { Retire(current, "Prompt input focus, source, edit or host admission changed. Late text discarded."); }
    }

    private void Transcript(object? sender, VoiceTranscriptEventArgs args)
    {
        Turn? current;
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            current = turn;
            if (current is null || current.Delivering || args.Generation != current.Generation
                || args.Generation != capture.CaptureGeneration || !Eligible(current)) { return; }
            current.Delivering = true;
            current.Delivered = delivered.Task;
            delivery = finished.Task;
        }
        capture.InvalidateCapture();
        _ = CompleteOperation(Deliver(current, args.Transcript, delivered), finished);
    }

    private static async Task CompleteOperation(Task operation, TaskCompletionSource finished)
    {
        try
        {
#pragma warning disable VSTHRD003 // Transfer the complete deferred publication and native cleanup outcome to the already published receipt.
            await operation.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            finished.TrySetResult();
        }
        catch (Exception exception) { finished.TrySetException(exception); }
    }

    private async Task Deliver(Turn current, string text, TaskCompletionSource delivered)
    {
        using var activity = BeginActivity(current, HostOperation.Request);
        try
        {
            await capture.StopAsync();
            if (!capture.IsCaptureQuiescent) { throw new InvalidOperationException("Prompt capture release is unconfirmed."); }
            if (!Eligible(current) || !await fresh(current.Draft.Source, current.Cancellation.Token) || !Eligible(current))
            { throw new OperationCanceledException("Prompt input changed before publication."); }
            RequireActivity(current);
            OwnedPromptTemplate.ValidateValue(text);
            await dispatcher.InvokeAsync(() =>
            {
                RequireActivity(current);
                if (!Eligible(current)) { throw new OperationCanceledException("Prompt input focus changed before publication."); }
                current.Apply(current.Name, text);
                return Task.CompletedTask;
            });
            activity.Complete(HostOperationOutcome.Completed);
            Retire(current, "Literal input updated in private memory. Preview the complete request again; separately review and confirm submission.");
        }
        catch (OperationCanceledException)
        {
            Retire(current, "Prompt input retired. No late value routed; existing native values remain.");
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            Retire(current, "Prompt input rejected or release failed. No successful handoff claimed; use native controls.");
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally { delivered.TrySetResult(); }
#pragma warning disable VSTHRD003 // Publication completes only after its terminal retirement joins native cleanup and cancellation disposal.
        Task released;
        lock (gate) { released = retirement; }
        await released;
#pragma warning restore VSTHRD003
    }

    private void Failed(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        Turn? current;
        lock (gate) { current = turn; if (current is null || args.Generation != current.Generation) { return; } }
        Retire(current, "Prompt recognition failed. No value admitted.");
    }

    private void Completed(object? sender, VoiceRecognitionCompletedEventArgs args)
    {
        Turn? current;
        lock (gate)
        {
            current = turn;
            if (current is null || current.Delivering || args.Generation != current.Generation) { return; }
        }
        Retire(current, "Prompt capture completed without an admitted literal value. Native values preserved.");
    }

    public void Retire(string reason) => Retire(null, reason);

    private void Retire(Turn? expected, string reason)
    {
        Turn? previous;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (expected is not null && !ReferenceEquals(expected, turn)) { return; }
            previous = turn;
            turn = null;
            status = reason;
            if (previous is not null) { retirement = released.Task; }
        }
        if (previous is not null)
        {
            capture.InvalidateCapture();
            previous.Cancellation.Cancel();
            previous.Expiry?.Dispose();
            _ = CompleteOperation(Release(previous), released);
        }
        Publish();
    }

    private async Task Release(Turn previous)
    {
        using var activity = BeginActivity(previous, HostOperation.Recovery);
        Exception? failure = null;
        try
        {
#pragma warning disable VSTHRD003 // Keep cancellation and target alive until this owner's native open and deferred delivery have joined.
            await previous.Opened.Task.ConfigureAwait(false);
            await capture.StopAsync().ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (!capture.IsCaptureQuiescent) { throw new InvalidOperationException("Prompt microphone release is unconfirmed."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception)
        {
            failure = exception;
            Failure(logger, exception.GetType().Name);
            lock (gate) { status = "Prompt microphone cleanup unconfirmed. Restart Kora before more voice input."; }
            activity.Complete(HostOperationOutcome.Failed);
            Publish();
        }
        finally
        {
#pragma warning disable VSTHRD003 // Even failed native cleanup must join this exact queued publication before releasing its cancellation resources.
            await previous.Delivered.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            previous.Expiry?.Dispose();
            await previous.Registration.DisposeAsync().ConfigureAwait(false);
            previous.Cancellation.Dispose();
        }
        if (failure is not null) { ExceptionDispatchInfo.Throw(failure); }
    }

    private static HostActivity BeginActivity(Turn current, HostOperation operation) =>
        HostActivity.BeginRoot(current.Request, HostActivityLayer.Application, operation,
            current.Context == default ? [] : [new ActivityLink(current.Context)]);

    private static void RequireActivity(Turn current)
    {
        if (HostActivity.RequireCurrent().Request != current.Request)
        { throw new InvalidOperationException("The exact prompt input activity expired or changed."); }
    }

    private void Publish() => dispatcher.Post(() => Changed?.Invoke(this, EventArgs.Empty));

    public void CloseAdmission()
    {
        closed = true;
        Retire("Prompt voice input closed. Late text discarded.");
        capture.TranscriptRecognized -= Transcript;
        capture.RecognitionFailed -= Failed;
        capture.RecognitionCompleted -= Completed;
    }

    public async ValueTask DisposeAsync()
    {
        CloseAdmission();
#pragma warning disable VSTHRD003 // The controller owns and joins its native cleanup without blocking UI dispatch.
        await Quiescence.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private sealed class Turn(PromptParameterDraft draft, string name, QuestionVoiceAdmission admission, Func<bool> focus,
        Action<string, string> apply, DateTimeOffset expiresAt, CancellationToken token)
    {
        public PromptParameterDraft Draft { get; } = draft;
        public long Revision { get; } = draft.InputRevision;
        public string Name { get; } = name;
        public QuestionVoiceAdmission Admission { get; } = admission;
        public Func<bool> Focus { get; } = focus;
        public Action<string, string> Apply { get; } = apply;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public CancellationTokenRegistration Registration { get; set; }
        public ITimer? Expiry { get; set; }
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Delivered { get; set; } = Task.CompletedTask;
        public bool Delivering { get; set; }
        public long Generation { get; set; } = -1;
        public HostRequest Request { get; } = HostRequest.Create(RequestOrigin.ActivatedVoice);
        public ActivityContext Context { get; set; }
    }
}
