using System.Diagnostics;

using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Interaction;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Definitions;

/// <summary>One live editor question, never a persisted conversation question or an approval.</summary>
public sealed partial class CustomizationVoiceClarification : IAsyncDisposable
{
    public static QuestionSpec Spec { get; } = new("Choose voice target", QuestionKind.SingleChoice,
        [new("refine", "Refine this artifact"), new("discuss", "Discuss this artifact"), new("cancel", "Cancel")]);
    private readonly Lock gate = new();
    private readonly CustomizationVoiceRequest request;
    private readonly Func<bool> exact;
    private readonly IQuestionVoiceRecognitionService? capture;
    private readonly Func<QuestionVoiceAdmission?> observe;
    private readonly ILogger logger;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<CustomizationVoiceChoice> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration registration;
    private readonly ITimer expiry;
    private readonly ActivityContext context;
    private Task opening = Task.CompletedTask;
    private bool started;
    private bool ownsCapture;
    private bool consuming;
    private bool closed;
    private long replyGeneration = -1;
    private QuestionVoiceAdmission? admission;
    private string status = "Microphone closed. Choose a button, or Answer by voice for this question only.";

    public CustomizationVoiceClarification(CustomizationVoiceRequest request, Func<bool> exact,
        IQuestionVoiceRecognitionService? capture, Func<QuestionVoiceAdmission?> observe, TimeProvider time,
        ILogger logger, CancellationToken token)
    {
        this.request = request;
        this.exact = exact;
        this.capture = capture;
        this.observe = observe;
        this.logger = logger;
        context = HostActivity.Current is { } host ? host.Activity!.Context : default;
        if (capture is not null)
        {
            capture.TranscriptRecognized += Transcript;
            capture.RecognitionFailed += Failed;
            capture.RecognitionCompleted += Completed;
            capture.CaptureStateChanged += CaptureChanged;
        }
        registration = token.Register(() => Cancel("Editor question withdrawn. Nothing was continued."));
        expiry = time.CreateTimer(_ => Cancel("Editor question expired. Start a fresh request."),
            null, TimeSpan.FromSeconds(75), Timeout.InfiniteTimeSpan);
        Completion = Complete();
    }

    public event EventHandler? Changed;
    public string Status { get { lock (gate) { return status; } } }
    public Task<CustomizationVoiceChoice> Completion { get; }
    public bool CanAnswerByVoice => !started && !answer.Task.IsCompleted && request.IsCurrent && exact()
        && capture is { IsCaptureQuiescent: true } && observe() is not null;

    public void StartVoice()
    {
        lock (gate)
        {
            if (!CanAnswerByVoice)
            { status = "Voice reply unavailable. Microphone closed; use the native choices."; }
            else
            {
                started = true;
                admission = observe();
                opening = Open();
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool Eligible() => request.IsAdmitted && exact() && !cancellation.IsCancellationRequested
        && (!ownsCapture || admission == observe());

    private async Task Open()
    {
        using var activity = BeginActivity(HostOperation.Presentation);
        try
        {
            if (!request.BeginHandover()) { throw new OperationCanceledException("Original capture retired."); }
            ownsCapture = true;
            await capture!.BeginReplyAsync(admission!.Microphone, Spec.Options.Select(option => option.Label),
                Eligible, cancellation.Token);
            replyGeneration = capture.CaptureGeneration;
            if (!Eligible() || !capture.AcceptCaptureGeneration(replyGeneration))
            { throw new OperationCanceledException("Exact reply activation retired."); }
            lock (gate) { status = "Speak Refine this artifact, Discuss this artifact, or Cancel. No other input is routed."; }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        { Cancel("Voice reply cancelled or admission changed. No operation continued."); activity.Complete(HostOperationOutcome.Cancelled); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            Cancel("Voice reply failed. No operation continued; start a fresh request or use native input.");
            activity.Complete(HostOperationOutcome.Failed);
        }
    }

    public void Choose(CustomizationVoiceChoice choice)
    {
        lock (gate)
        {
            if (answer.Task.IsCompleted) { return; }
            if (!Enum.IsDefined(choice) || !Eligible() || !ownsCapture && !request.IsCurrent
                || replyGeneration >= 0 && capture!.CaptureGeneration != replyGeneration)
            { Cancel("Editor question is no longer current. Nothing was continued."); return; }
            consuming = true;
            if (choice is CustomizationVoiceChoice.Cancel) { status = "Editor authoring cancelled. Nothing was continued."; }
            answer.TrySetResult(choice);
        }
    }

    public void Cancel(string reason)
    {
        lock (gate)
        {
            if (closed) { return; }
            request.Retire();
            status = reason;
            consuming = true;
            answer.TrySetResult(CustomizationVoiceChoice.Cancel);
            cancellation.Cancel();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Revalidate()
    {
        if (closed) { return; }
        if (!Eligible() || !ownsCapture && !request.IsCurrent
            || replyGeneration >= 0 && !consuming && capture!.CaptureGeneration != replyGeneration)
        { Cancel("Editor, question, capture or voice admission changed. Nothing was continued."); }
    }

    private void Transcript(object? sender, VoiceTranscriptEventArgs args)
    {
        lock (gate)
        {
            if (!ownsCapture || consuming || args.Generation != replyGeneration
                || args.Generation != capture!.CaptureGeneration || !Eligible()) { return; }
            var parsed = QuestionVoiceAnswer.Parse(Spec, args.Transcript);
            if (parsed is null)
            { Cancel("Reply did not match this question. Nothing was routed; start a fresh request."); return; }
            Choose(parsed.Choices[0] switch
            {
                "refine" => CustomizationVoiceChoice.Refine,
                "discuss" => CustomizationVoiceChoice.Discuss,
                _ => CustomizationVoiceChoice.Cancel,
            });
        }
    }

    private void Failed(object? sender, VoiceRecognitionFailureEventArgs args)
    {
        if (ownsCapture && !consuming && args.Generation == replyGeneration) { Cancel("Reply recognition failed. Nothing was continued."); }
    }

    private void Completed(object? sender, VoiceRecognitionCompletedEventArgs args)
    {
        if (ownsCapture && !consuming && args.Generation == replyGeneration)
        { Cancel("Reply capture ended without an answer. Nothing was continued."); }
    }

    private void CaptureChanged(object? sender, VoiceCaptureStateChangedEventArgs args)
    {
        if (ownsCapture && !consuming && replyGeneration >= 0 && capture!.CaptureGeneration != replyGeneration)
        { Cancel("Exact editor reply capture was retired. Nothing was continued."); }
    }

    private async Task<CustomizationVoiceChoice> Complete()
    {
#pragma warning disable VSTHRD003 // Joins this exact question's response and capture opening before native release.
        var choice = await answer.Task.ConfigureAwait(false);
        Task opened;
        lock (gate) { opened = opening; }
        await opened.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        using var activity = BeginActivity(HostOperation.Recovery);
        try
        {
            if (ownsCapture)
            {
                if (replyGeneration >= 0 && capture!.CaptureGeneration != replyGeneration) { request.Retire(); }
                var stopping = capture!.StopAsync();
                var releasedGeneration = capture.CaptureGeneration;
                await stopping.ConfigureAwait(false);
                if (!capture.IsCaptureQuiescent || releasedGeneration != capture.CaptureGeneration)
                { throw new InvalidOperationException("Exact reply capture release is unconfirmed."); }
                request.CompleteHandover(releasedGeneration);
            }
            if (choice == CustomizationVoiceChoice.Cancel || !Eligible() || !request.IsCurrent)
            {
                request.Retire();
                activity.Complete(HostOperationOutcome.Cancelled);
                return CustomizationVoiceChoice.Cancel;
            }
            activity.Complete(HostOperationOutcome.Completed);
            return choice;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            request.Retire();
            lock (gate) { status = "Editor reply cleanup unconfirmed. No operation continued; restart before more capture."; }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            lock (gate) { closed = true; }
            if (capture is not null)
            {
                capture.TranscriptRecognized -= Transcript;
                capture.RecognitionFailed -= Failed;
                capture.RecognitionCompleted -= Completed;
                capture.CaptureStateChanged -= CaptureChanged;
            }
            await expiry.DisposeAsync().ConfigureAwait(false);
            await registration.DisposeAsync().ConfigureAwait(false);
            cancellation.Dispose();
        }
    }

    private HostActivity BeginActivity(HostOperation operation) =>
        HostActivity.BeginRoot(request.Request, HostActivityLayer.Application, operation,
            context == default ? [] : [new ActivityLink(context)]);

    public async ValueTask DisposeAsync()
    {
        if (!Completion.IsCompleted) { Cancel("Editor question closed. No late reply is accepted."); }
#pragma warning disable VSTHRD003 // Retains question, subscription and cancellation ownership until its complete capture receipt joins.
        await Completion.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }
}
