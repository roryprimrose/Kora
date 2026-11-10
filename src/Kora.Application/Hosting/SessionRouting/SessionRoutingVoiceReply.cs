using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;

using Kora.Application.Voice;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Diagnostics;
using Kora.Core.Interaction;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting.SessionRouting;

/// <summary>One explicitly armed closed routing question; completion includes physical capture release.</summary>
public sealed partial class SessionRoutingVoiceReply : IAsyncDisposable
{
    public enum SessionRoutingVoiceChoice { Destination, New, Submit, Correct, Cancel }
    public sealed record SessionRoutingVoiceAnswer(SessionRoutingVoiceChoice Choice, SessionRouteDescriptor? Destination = null);
    private readonly Lock gate = new();
    private readonly SessionRoutingVoiceRequest request;
    private readonly Func<bool> exact;
    private readonly Func<QuestionVoiceAdmission?> observe;
    private readonly ILogger logger;
    private readonly Dictionary<string, SessionRoutingVoiceAnswer> answers;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<SessionRoutingVoiceAnswer?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private QuestionVoiceAdmission? admission;
    private bool started;
    private bool consuming;
    private bool ownsCapture;
    private long replyGeneration = -1;
    private bool closed;
    private string status = "Microphone closed. Answer by voice is an explicit choice for this question only.";

    private SessionRoutingVoiceReply(SessionRoutingVoiceRequest request, SessionRoutingReview review,
        QuestionSpec spec, Dictionary<string, SessionRoutingVoiceAnswer> answers, Func<bool> exact,
        Func<QuestionVoiceAdmission?> observe, ILogger logger)
    {
        if (!string.Equals(request.Body, review.Body, StringComparison.Ordinal) || request.Request.Origin != review.Origin)
        { throw new InvalidOperationException("Voice replies require the exact unchanged original routing request."); }
        this.request = request;
        this.answers = answers;
        this.exact = exact;
        this.observe = observe;
        this.logger = logger;
        Spec = spec;
    }

    public static SessionRoutingVoiceReply ForDestinations(SessionRoutingVoiceRequest request, SessionRoutingReview review,
        IEnumerable<SessionRouteDescriptor> presented, Func<bool> exact, Func<QuestionVoiceAdmission?> observe, ILogger logger)
    {
        var rows = presented.ToArray();
        static bool SamePresentation(SessionRouteDescriptor[] actual, IEnumerable<SessionRouteDescriptor> expected) =>
            actual.SequenceEqual(expected, ReferenceEqualityComparer.Instance);
        if (rows.Distinct().Count() != rows.Length
            || rows.Any(row => !review.Snapshot.Descriptors.Any(item => ReferenceEquals(item, row)))
            || !SamePresentation(rows, review.Candidates) && !SamePresentation(rows, review.Snapshot.Descriptors))
        { throw new InvalidOperationException("Only exact host-held presented destination objects may be voice choices."); }
        var options = rows.Select((_, index) => new QuestionOption("destination-" + index.ToString(CultureInfo.InvariantCulture),
            (index + 1).ToString(CultureInfo.InvariantCulture))).Append(new("new", "New")).Append(new("cancel", "Cancel")).ToArray();
        var choices = rows.Select((row, index) => new KeyValuePair<string, SessionRoutingVoiceAnswer>(
            options[index].Id, new(SessionRoutingVoiceChoice.Destination, row))).ToDictionary(StringComparer.Ordinal);
        choices.Add("new", new(SessionRoutingVoiceChoice.New));
        choices.Add("cancel", new(SessionRoutingVoiceChoice.Cancel));
        return new(request, review, new("Choose one exact presented ordinal, New, or Cancel", QuestionKind.SingleChoice, options),
            choices, exact, observe, logger);
    }

    public static SessionRoutingVoiceReply ForConfirmation(SessionRoutingVoiceRequest request, SessionRoutingReview review,
        SessionRoutingSelection selection, Func<bool> exact, Func<QuestionVoiceAdmission?> observe, ILogger logger)
    {
        if (!ReferenceEquals(selection.Review, review))
        { throw new InvalidOperationException("Confirmation requires this exact routing review."); }
        return new(request, review, new("Submit original request, Correct destination, or Cancel", QuestionKind.SingleChoice,
            [new("submit", "Submit original request"), new("correct", "Correct destination"), new("cancel", "Cancel")]),
            new(StringComparer.Ordinal) { ["submit"] = new(SessionRoutingVoiceChoice.Submit), ["correct"] = new(SessionRoutingVoiceChoice.Correct),
                ["cancel"] = new(SessionRoutingVoiceChoice.Cancel) }, exact, observe, logger);
    }

    public QuestionSpec Spec { get; }
    public event EventHandler? Changed;
    public string Status { get { lock (gate) { return status; } } }
    public Task<SessionRoutingVoiceAnswer?> Completion { get; private set; } = Task.FromResult<SessionRoutingVoiceAnswer?>(null);
    public bool CanAnswerByVoice => !started && request.IsCurrent && exact()
        && request.Capture is { IsCaptureQuiescent: true } && observe() is not null;

    public Task<SessionRoutingVoiceAnswer?> StartAsync()
    {
        lock (gate)
        {
            if (started) { throw new InvalidOperationException("This exact routing reply was already activated."); }
            if (!CanAnswerByVoice)
            {
                status = "Voice reply unavailable. Microphone closed; use the native choices.";
                return Completion;
            }
            started = true;
            admission = observe();
            Completion = RunAsync();
            return Completion;
        }
    }

    private bool Eligible() => request.IsLive && exact() && !cancellation.IsCancellationRequested
        && admission is not null && admission == observe();

    private async Task<SessionRoutingVoiceAnswer?> RunAsync()
    {
        using var activity = HostActivity.BeginRoot(request.Request, HostActivityLayer.Application, HostOperation.Presentation,
            HostActivity.Current?.Activity is { } cause ? [new ActivityLink(cause.Context)] : []);
        var capture = request.Capture!;
        capture.TranscriptRecognized += Transcript;
        capture.RecognitionFailed += Failed;
        capture.RecognitionCompleted += Completed;
        capture.CaptureStateChanged += CaptureChanged;
        SessionRoutingVoiceAnswer? result = null;
        ExceptionDispatchInfo? releaseFailure = null;
        var failed = false;
        try
        {
            if (!request.BeginHandover() || !Eligible()) { throw new OperationCanceledException("Original routing input retired."); }
            ownsCapture = true;
            await request.OpenAsync(admission!.Microphone, Spec.Options.Select(option => option.Label), Eligible, cancellation.Token).ConfigureAwait(false);
            lock (gate) { replyGeneration = capture.CaptureGeneration; }
            request.Acknowledge(replyGeneration);
            if (!Eligible() || !capture.AcceptCaptureGeneration(replyGeneration))
            { throw new OperationCanceledException("Exact routing reply generation was not acknowledged."); }
            lock (gate) { status = "Speak exactly " + string.Join(", ", Spec.Options.Select(option => option.Label)) + ". No other input is routed."; }
            Changed?.Invoke(this, EventArgs.Empty);
#pragma warning disable VSTHRD003 // The exact one-use utterance receipt, not a separate capture operation.
            result = await answer.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested) { request.Retire(); }
            Cancel("Voice reply cancelled or original admission changed. Nothing was submitted.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failed = true;
            Failure(logger, exception.GetType().Name);
            request.Retire();
            Cancel("Voice reply failed. No fallback or submission was performed.");
        }
        finally
        {
            lock (gate) { consuming = true; }
            try
            {
                if (ownsCapture) { await request.ReleaseAsync(replyGeneration).ConfigureAwait(false); }
                if (admission != observe()) { request.Retire(); }
                if (!request.IsCurrent || !exact()) { result = null; }
                activity.Complete(failed ? HostOperationOutcome.Failed
                    : result is null ? HostOperationOutcome.Cancelled : HostOperationOutcome.Completed);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                request.Retire();
                lock (gate) { status = "Routing reply cleanup unconfirmed. No submission; restart before more capture."; }
                Failure(logger, exception.GetType().Name);
                activity.Complete(HostOperationOutcome.Failed);
                releaseFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                capture.TranscriptRecognized -= Transcript;
                capture.RecognitionFailed -= Failed;
                capture.RecognitionCompleted -= Completed;
                capture.CaptureStateChanged -= CaptureChanged;
                lock (gate) { closed = true; }
                cancellation.Dispose();
            }
        }
        releaseFailure?.Throw();
        return result;
    }

    public void Cancel(string reason = "Routing reply withdrawn. No late reply is accepted.")
    {
        lock (gate)
        {
            if (closed) { return; }
            consuming = true;
            status = reason;
            answer.TrySetResult(null);
            cancellation.Cancel();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Revalidate()
    {
        if (!started || closed || consuming) { return; }
        if (!Eligible() || replyGeneration >= 0 && request.Capture!.CaptureGeneration != replyGeneration)
        {
            request.Retire();
            Cancel("Original routing question, source or capture changed. Nothing was submitted.");
        }
    }

    private void Transcript(object? sender, VoiceTranscriptEventArgs args)
    {
        lock (gate)
        {
            if (!ownsCapture || consuming || args.Generation != replyGeneration
                || request.Capture!.CaptureGeneration != replyGeneration) { return; }
            if (!Eligible()) { request.Retire(); Cancel(); return; }
            var parsed = QuestionVoiceAnswer.Parse(Spec, args.Transcript);
            var option = parsed is null ? null : Spec.Options.Single(item => string.Equals(item.Id, parsed.Choices[0], StringComparison.Ordinal));
            if (option is null || !string.Equals(option.Label, args.Transcript.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                request.Retire();
                Cancel("Reply did not match this exact routing question. No command, guess or submission was performed.");
                return;
            }
            consuming = true;
            answer.TrySetResult(answers[option.Id]);
        }
    }

    private void Failed(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        if (ownsCapture && !consuming && args.Generation == replyGeneration)
        { request.Retire(); Cancel("Routing reply recognition failed. Nothing was submitted."); }
    }

    private void Completed(object? sender, VoiceRecognitionCompletedEventArgs args)
    {
        if (ownsCapture && !consuming && args.Generation == replyGeneration)
        { request.Retire(); Cancel("Routing capture ended without an exact answer. Nothing was submitted."); }
    }

    private void CaptureChanged(object? sender, VoiceCaptureStateChangedEventArgs args) => Revalidate();

    public async ValueTask DisposeAsync()
    {
        Cancel();
#pragma warning disable VSTHRD003 // Keep reply ownership through opening, recognition and physically joined release.
        await Completion.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        if (!started) { closed = true; cancellation.Dispose(); }
    }
}
