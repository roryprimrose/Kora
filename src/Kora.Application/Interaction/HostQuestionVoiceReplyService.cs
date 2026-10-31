using System.Diagnostics;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

/// <summary>The native question owner's single application-wide, volatile reply lifetime.</summary>
public sealed partial class HostQuestionVoiceReplyService : IAsyncDisposable
{
    private static readonly TimeSpan SpeechStartWindow = TimeSpan.FromSeconds(15);
    private readonly Lock gate = new();
    private readonly HostQuestionService questions;
    private readonly IQuestionVoiceRecognitionService capture;
    private readonly TimeProvider time;
    private readonly IUiDispatcher dispatcher;
    private readonly Func<QuestionVoiceAdmission?> observe;
    private readonly Func<string, Func<bool>, CancellationToken, Task> speak;
    private readonly ILogger<HostQuestionVoiceReplyService> logger;
    private readonly Func<string, Task<bool>>? tryStopListening;
    private readonly Func<IEnumerable<string>>? listeningStopPhrases;
    private readonly Func<ConversationalReplyState?>? conversationalPreference;
    private Turn? turn;
    private Task retirement = Task.CompletedTask;
    private Task delivery = Task.CompletedTask;
    private bool disposed;
    private string status = "Microphone closed. Answer by voice requires enabled listening and current voice readiness.";

    public HostQuestionVoiceReplyService(HostQuestionService questions, IQuestionVoiceRecognitionService capture,
        TimeProvider time, IUiDispatcher dispatcher, Func<QuestionVoiceAdmission?> observe,
        Func<string, Func<bool>, CancellationToken, Task> speak, ILogger<HostQuestionVoiceReplyService> logger,
        Func<string, Task<bool>>? tryStopListening = null, Func<IEnumerable<string>>? listeningStopPhrases = null,
        Func<ConversationalReplyState?>? conversationalPreference = null)
    {
        this.questions = questions;
        this.capture = capture;
        this.time = time;
        this.dispatcher = dispatcher;
        this.observe = observe;
        this.speak = speak;
        this.logger = logger;
        this.tryStopListening = tryStopListening;
        this.listeningStopPhrases = listeningStopPhrases;
        this.conversationalPreference = conversationalPreference;
        questions.ReplyTargetChanging += OnTargetChanging;
        capture.TranscriptRecognized += OnTranscript;
        capture.ReplySpeechStarted += OnSpeechStarted;
        capture.RecognitionFailed += OnFailed;
        capture.RecognitionCompleted += OnCompleted;
    }

    public event EventHandler? Changed;
    public string Status { get { lock (gate) { return status; } } }
    public bool IsActive { get { lock (gate) { return turn is not null; } } }
#pragma warning disable VSTHRD003 // Both tasks are owned asynchronous lifecycle receipts; callers must never block the dispatcher.
    public Task Quiescence { get { lock (gate) { return Task.WhenAll(retirement, delivery); } } }
#pragma warning restore VSTHRD003

    public Task OpenConversationalAsync(HostQuestionRecord question, Func<bool> isExactVisiblePresenter,
        Action<HostInteractionDecision> accepted, CancellationToken cancellationToken)
    {
        var preference = conversationalPreference?.Invoke();
        if (preference is not { Enabled: true } || !IsConversationalQuestion(question))
        {
            return Task.CompletedTask;
        }
        return OpenCoreAsync(question, () => conversationalPreference!() == preference && isExactVisiblePresenter(),
            accepted, afterPlayback: true, conversational: true, cancellationToken);
    }

    private static bool IsConversationalQuestion(HostQuestionRecord question) =>
        question.Proposal is null && (string.Equals(question.Spec.SourceId, "host", StringComparison.Ordinal)
            && question.Spec.Purpose is "clarification" or "local-version"
            || string.Equals(question.Spec.SourceId, LocalModelQuestionPolicy.SourceId, StringComparison.Ordinal)
            && string.Equals(question.Spec.Purpose, LocalModelQuestionPolicy.Purpose, StringComparison.Ordinal));

    public Task OpenAsync(HostQuestionRecord question, Func<bool> isExactVisiblePresenter,
        Action<HostInteractionDecision> accepted, bool afterPlayback, CancellationToken cancellationToken) =>
        OpenCoreAsync(question, isExactVisiblePresenter, accepted, afterPlayback, conversational: false, cancellationToken);

    public Task OpenFieldAsync(HostQuestionRecord question, string fieldId, Func<bool> isExactVisiblePresenter,
        Action<QuestionFieldAnswer> edited, CancellationToken cancellationToken)
    {
        var field = question.Spec.Fields.SingleOrDefault(item => string.Equals(item.Id, fieldId, StringComparison.Ordinal));
        if (question.Spec.Kind != QuestionKind.Form || question.Proposal is not null || field is null)
        { throw new InvalidOperationException("Voice field entry requires one exact ordinary form field."); }
        return OpenCoreAsync(question, isExactVisiblePresenter, static _ => { }, afterPlayback: true,
            conversational: false, cancellationToken, field, edited);
    }

    private async Task OpenCoreAsync(HostQuestionRecord question, Func<bool> isExactVisiblePresenter,
        Action<HostInteractionDecision> accepted, bool afterPlayback, bool conversational, CancellationToken cancellationToken,
        QuestionField? field = null, Action<QuestionFieldAnswer>? edited = null)
    {
        if (question.Spec.Kind == QuestionKind.Form && field is null)
        {
            Retire("Choose a displayed form field for voice entry. Field entry never submits the form.");
            return;
        }
        if (capture is HandsFreeVoiceRecognitionService handsFree) { await handsFree.PauseWakeForReplyAsync(); }
        Turn opened;
        var admission = observe();
        var presented = isExactVisiblePresenter();
        var quiescent = capture.IsCaptureQuiescent;
        if (admission is null)
        {
            Retire("Voice reply unavailable. Microphone closed; review voice readiness before trying again.");
            return;
        }
        var denied = false;
        lock (gate)
        {
            if (disposed || turn is not null || !retirement.IsCompletedSuccessfully
                || !quiescent || !presented)
            {
                denied = true;
            }
            opened = new(question, admission, isExactVisiblePresenter, accepted, field, edited, cancellationToken);
            if (!denied)
            {
                turn = opened;
                status = afterPlayback ? "Speaking exact question. Microphone closed; no barge-in." : "Revalidating exact question. Microphone closed.";
            }
        }
        if (denied)
        {
            opened.Cancellation.Dispose();
            Retire("Voice reply unavailable or competing target. Microphone closed; the native question remains answerable.");
            return;
        }
        Publish();
        using var activity = BeginQuestionActivity(question.Key.Request, HostOperation.Presentation);
        opened.Cancellation.Token.Register(() => Retire(opened, "Reply cancelled. Microphone closed; native question remains answerable."));
        try
        {
            var reviewed = await questions.ReviewReplyAsync(question.Key, question.SessionGeneration,
                () => Eligible(opened), opened.Cancellation.Token);
            if (reviewed.Outcome != HostInteractionOutcome.Presented || reviewed.Question is not { } canonical
                || canonical.Key != question.Key || canonical.SessionGeneration != question.SessionGeneration)
            {
                Retire(opened, "Exact question is unavailable for voice. No capture opened; use native controls.");
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            if (conversational && !IsConversationalQuestion(canonical))
            {
                Retire(opened, "This question requires explicit native interaction. No automatic capture opened.");
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            if (field is not null && !canonical.Spec.Fields.Any(item => item.Matches(field)))
            {
                Retire(opened, "The exact form field changed. No capture opened.");
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            var presentationContext = HostActivity.RequireCurrent().Activity!.Context;
            lock (gate)
            {
                opened.Question = canonical;
                opened.PresentationContext = presentationContext;
            }
            opened.Expiry = time.CreateTimer(_ => Retire(opened, "Question expired. Microphone closed; start a fresh question."),
                null, canonical.ExpiresAt - time.GetUtcNow(), Timeout.InfiniteTimeSpan);
            opened.Cancellation.Token.Register(opened.Expiry.Dispose);
            if (afterPlayback)
            {
                var readback = field is not null
                    ? field.Label + (field.Kind == QuestionKind.Text
                        ? $". Required text, maximum {field.MaximumLength} characters and {field.MaximumUtf8Bytes} UTF-8 bytes."
                        : ". Required choice. " + string.Join(" ", field.Options.Select((option, index) => $"Option {index + 1}: {option.Label}.")))
                        + " Voice entry edits this field only. Review and explicitly Submit the whole form using native controls."
                    : canonical.Spec.Options.IsEmpty
                    || !string.Equals(canonical.Spec.SourceId, LocalModelQuestionPolicy.SourceId, StringComparison.Ordinal)
                    ? canonical.Spec.Text
                    : canonical.Spec.Text + " " + string.Join(" ", canonical.Spec.Options.Select((option, index) =>
                        "Option " + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + option.Label + "."))
                        + (canonical.Spec.Kind == QuestionKind.MultipleChoice
                            ? " Choose " + canonical.Spec.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                + " to " + canonical.Spec.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                + " options. Say option numbers separated by and, or use the checkboxes."
                            : " Choose one option.");
                await speak(readback, () => Eligible(opened), opened.Cancellation.Token);
            }
            if (!Eligible(opened))
            {
                Retire(opened, "Question or voice admission changed before capture. Microphone closed.");
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            lock (gate)
            {
                opened.Cancellation.Token.ThrowIfCancellationRequested();
                opened.Deadline = time.GetUtcNow().Add(SpeechStartWindow);
                status = "Awaiting reply - 15 seconds to begin. Local one-question capture; Cancel reply stops it.";
                opened.Timer = time.CreateTimer(_ => Tick(opened), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                opened.Cancellation.Token.Register(opened.Timer.Dispose);
            }
            Publish();
            await capture.BeginReplyAsync(opened.Admission.Microphone,
                (field?.Options ?? canonical.Spec.Options).Select(option => option.Label).Concat(listeningStopPhrases?.Invoke() ?? []),
                () => Eligible(opened), opened.Cancellation.Token);
            lock (gate) { opened.Generation = capture.Generation; }
            if (!Eligible(opened) || !capture.AcceptCaptureGeneration(opened.Generation))
            {
                Retire(opened, "Reply activation retired before acknowledgement. Microphone closed.");
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            Retire(opened, "Reply cancelled or admission withdrawn. Microphone closed; native question remains available.");
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReplyFailure(logger, exception.GetType().FullName);
            Retire(opened, "Voice reply failed. Microphone closed; use native controls. Explicit voice recovery may be required.");
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally { opened.Opened.TrySetResult(); }
    }

    private bool Eligible(Turn candidate)
    {
        var presented = candidate.Presented();
        var admission = observe();
        var now = time.GetUtcNow();
        lock (gate)
        {
            return !disposed && ReferenceEquals(turn, candidate) && !candidate.Cancellation.IsCancellationRequested
                && presented && admission == candidate.Admission && candidate.Question.ExpiresAt > now;
        }
    }

    public void Revalidate()
    {
        Turn? current;
        lock (gate) { current = turn; }
        if (current is not null && !Eligible(current)) { Retire(current, "Voice admission changed. Microphone closed."); }
    }

    public void Retire(string reason)
        => Retire(null, reason);

    private void Retire(Turn? expected, string reason)
    {
        Turn? previous;
        Task earlier;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (expected is not null && !ReferenceEquals(expected, turn)) { return; }
            previous = turn;
            turn = null;
            status = reason;
            earlier = retirement;
            if (previous is not null)
            {
                retirement = finished.Task;
            }
        }
        if (previous is not null)
        {
            previous.Timer?.Dispose();
            previous.Expiry?.Dispose();
            capture.InvalidateCapture();
            previous.Cancellation.Cancel();
            _ = CompleteRetirementAsync(CloseAsync(previous, earlier), finished);
        }
        Publish();
    }

    private static async Task CompleteRetirementAsync(Task closing, TaskCompletionSource finished)
    {
        try
        {
#pragma warning disable VSTHRD003 // Transfer the owned closure outcome to the receipt published before starting native release.
            await closing.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            finished.TrySetResult();
        }
        catch (Exception exception)
        {
            // Propagate even fatal/observer failures to the quiescence receipt; never leave a success-shaped or hung receipt.
            finished.TrySetException(exception);
        }
    }

    private async Task CloseAsync(Turn previous, Task earlier)
    {
        using var activity = BeginQuestionActivity(previous.Question.Key.Request, HostOperation.Recovery, previous);
        Exception? failure = null;
        try
        {
            await capture.StopAsync().ConfigureAwait(false);
#pragma warning disable VSTHRD003 // Event-driven native/open/delivery receipts are owned and awaited without a UI context.
            await earlier.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception;
            ReplyFailure(logger, exception.GetType().FullName);
            lock (gate) { status = "Microphone cleanup unconfirmed. Restart Kora before another voice reply."; }
            Publish();
        }
        finally
        {
#pragma warning disable VSTHRD003 // These completion sources cannot fault and keep cancellation alive through the owned callbacks.
            await previous.Opened.Task.ConfigureAwait(false);
            await previous.Delivered.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            previous.Cancellation.Dispose();
        }
        if (failure is null)
        {
            if (capture is HandsFreeVoiceRecognitionService handsFree) { await handsFree.ResumeAfterActivatedInputAsync(); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        else
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw new InvalidOperationException("Reply capture closure is unconfirmed.", failure);
        }
    }

    private void Tick(Turn candidate)
    {
        var eligible = Eligible(candidate);
        bool timedOut;
        lock (gate)
        {
            if (!ReferenceEquals(turn, candidate)) { return; }
            if (!eligible) { timedOut = true; }
            else { timedOut = !candidate.SpeechStarted && time.GetUtcNow() >= candidate.Deadline; }
            status = candidate.SpeechStarted ? "Capturing reply - one local utterance, maximum 60 seconds. Cancel reply stops capture."
                : $"Awaiting reply - {Math.Ceiling((candidate.Deadline - time.GetUtcNow()).TotalSeconds)} seconds to begin. Cancel reply stops capture.";
        }
        if (timedOut)
        {
            Retire(candidate, "Reply start timed out. Microphone closed; question remains answerable. Choose Answer by voice to try again.");
            return;
        }
        Publish();
    }

    private void OnSpeechStarted(object? sender, VoiceCaptureStateChangedEventArgs args)
    {
        var admission = observe();
        var generation = capture.Generation;
        Turn? current;
        bool timedOut;
        lock (gate)
        {
            current = turn;
            if (current is null || args.Generation != generation || admission != current.Admission || !current.Presented()
                || current.Question.ExpiresAt <= time.GetUtcNow()) { return; }
            timedOut = time.GetUtcNow() >= current.Deadline;
            if (!timedOut)
            {
                current.SpeechStarted = true;
                status = "Capturing reply - one local utterance, maximum 60 seconds. Cancel reply stops capture.";
            }
        }
        if (timedOut) { Retire(current, "Reply start timed out. Microphone closed; question remains answerable."); }
        Publish();
    }

    private void OnTranscript(object? sender, VoiceTranscriptEventArgs args)
    {
        var admission = observe();
        var generation = capture.Generation;
        Turn? current;
        lock (gate)
        {
            current = turn;
            if (current is null || current.Submitting || args.Generation != current.Generation
                || args.Generation != generation || admission != current.Admission || !current.Presented()
                || current.Question.ExpiresAt <= time.GetUtcNow()
                || !current.SpeechStarted && time.GetUtcNow() >= current.Deadline) { return; }
            current.Submitting = true;
            current.Delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            current.Timer!.Dispose();
            status = "Microphone closed. Submitting only the exact schema-valid question answer.";
        }
        capture.InvalidateCapture();
        var submitted = DeliverAsync(current, args.Transcript);
        lock (gate) { delivery = submitted; }
        Publish();
    }

    private async Task DeliverAsync(Turn current, string transcript)
    {
        using var activity = BeginQuestionActivity(current.Question.Key.Request, HostOperation.Request, current);
        var outcome = HostOperationOutcome.Completed;
        try
        {
            await dispatcher.InvokeAsync(async () =>
            {
                if (!Eligible(current)) { throw new OperationCanceledException("Reply admission retired before dispatch."); }
                if (tryStopListening is not null && await tryStopListening(transcript))
                {
                    Retire(current, "Listening disabled. Native question remains answerable; no answer was submitted.");
                    return;
                }
                var answer = current.Field is { } field
                    ? QuestionVoiceAnswer.Parse(field.VoiceSpec(), transcript, current.Admission.ActivationName)
                    : QuestionVoiceAnswer.Parse(current.Question, transcript, current.Admission.ActivationName);
                if (answer is null)
                {
                    Retire(current, "Reply did not match this question's schema. Nothing was routed or submitted. Microphone closed; use native controls or try again.");
                    return;
                }
                await capture.StopAsync();
                if (current.Field is { } targetField)
                {
                    var value = new QuestionFieldAnswer(targetField.Id, answer.Text, answer.Choices.SingleOrDefault());
                    if (!targetField.Accepts(value, submitting: true) || !Eligible(current))
                    {
                        Retire(current, "Field entry was refused or its draft changed. Nothing was saved or submitted.");
                        return;
                    }
                    current.Edited!(value);
                    Retire(current, "Field edited locally. Review all fields and explicitly Submit; nothing was saved or submitted.");
                    return;
                }
                var decision = await questions.SubmitReplyAsync(current.Question.Key, current.Question.SessionGeneration,
                    answer, () => Eligible(current), current.Cancellation.Token);
                if (decision.Outcome != HostInteractionOutcome.Answered)
                {
                    outcome = HostOperationOutcome.Failed;
                    Retire(current, "Reply was not accepted for this exact question. Microphone closed; review native question status before trying again.");
                    return;
                }
                if (Eligible(current)) { current.Accepted(decision); }
            });
            activity.Complete(outcome);
            Retire(current, capture.IsAmbientListeningAvailable
                ? "Reply turn ended. Fresh policy may restore wake-only listening; no answer or audio is replayed."
                : "Reply turn ended. Microphone closed; ordinary explicit push-to-talk readiness only.");
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            Retire(current, "Reply retired before acceptance. Microphone closed; no late answer is routed.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReplyFailure(logger, exception.GetType().FullName);
            activity.Complete(HostOperationOutcome.Failed);
            Retire(current, "Reply commit or cleanup failed. No successful answer claimed; inspect the native question before recovery.");
        }
        finally { current.Delivered.TrySetResult(); }
    }

    private HostActivity BeginQuestionActivity(HostRequest request, HostOperation operation, Turn? candidate = null)
    {
        var current = HostActivity.Current;
        if (operation == HostOperation.Presentation && current?.Request == request)
        {
            return HostActivity.BeginChild(HostActivityLayer.Application, operation);
        }
        ActivityContext? presentationContext;
        lock (gate) { presentationContext = candidate?.PresentationContext; }
        // Deferred causes remain links; incoming correlation cannot choose the question's request.
        List<ActivityLink> causes = [];
        if (presentationContext is { } presented) { causes.Add(new(presented)); }
        if (current?.Activity is { } cause && cause.Context != presentationContext) { causes.Add(new(cause.Context)); }
        return HostActivity.BeginRoot(request, HostActivityLayer.Application, operation, causes);
    }

    private void OnTargetChanging(HostQuestionKey? key)
    {
        Turn? current;
        lock (gate) { current = turn; }
        if (current is not null && (key is null || current.Question.Key.QuestionId == key.QuestionId))
        {
            Retire(current, "Exact question changed. Microphone closed.");
        }
    }
    private void OnFailed(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        Turn? current;
        lock (gate) { current = turn; }
        if (current is { Submitting: false } && args.Generation == current.Generation)
        {
            if (capture is HandsFreeVoiceRecognitionService handsFree) { handsFree.RequireExplicitRecovery(); }
            Retire(current, "Local recognition failed. Microphone closed; use native controls.");
        }
    }
    private void OnCompleted(object? sender, VoiceRecognitionCompletedEventArgs args)
    {
        Turn? current;
        lock (gate) { current = turn; }
        if (current is { Submitting: false } && args.Generation == current.Generation)
        {
            Retire(current, "Reply capture ended without an accepted answer. Microphone closed; native question remains answerable.");
        }
    }
    private void Publish() => Changed?.Invoke(this, EventArgs.Empty);

    public void CloseAdmission()
    {
        lock (gate) { disposed = true; }
        Retire("Host closed. Microphone closed; no reply is restored on restart.");
        questions.ReplyTargetChanging -= OnTargetChanging;
        capture.TranscriptRecognized -= OnTranscript;
        capture.ReplySpeechStarted -= OnSpeechStarted;
        capture.RecognitionFailed -= OnFailed;
        capture.RecognitionCompleted -= OnCompleted;
    }

    public async ValueTask DisposeAsync()
    {
        CloseAdmission();
#pragma warning disable VSTHRD003 // Await this owner's event-driven closure, never synchronously wait.
        await Quiescence.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private sealed class Turn(HostQuestionRecord question, QuestionVoiceAdmission admission,
        Func<bool> presented, Action<HostInteractionDecision> accepted,
        QuestionField? field, Action<QuestionFieldAnswer>? edited, CancellationToken cancellation)
    {
        public HostQuestionRecord Question { get; set; } = question;
        public ActivityContext? PresentationContext { get; set; }
        public QuestionVoiceAdmission Admission { get; } = admission;
        public Func<bool> Presented { get; } = presented;
        public Action<HostInteractionDecision> Accepted { get; } = accepted;
        public QuestionField? Field { get; } = field;
        public Action<QuestionFieldAnswer>? Edited { get; } = edited;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        public ITimer? Timer { get; set; }
        public ITimer? Expiry { get; set; }
        public DateTimeOffset Deadline { get; set; }
        public long Generation { get; set; } = -1;
        public bool SpeechStarted { get; set; }
        public bool Submitting { get; set; }
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Delivered { get; set; } = Completed();
        private static TaskCompletionSource Completed()
        {
            var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            result.SetResult();
            return result;
        }
    }
}
