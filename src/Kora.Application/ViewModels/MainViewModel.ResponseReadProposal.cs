using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private const string ResponseReadProposalPolicy =
        "Kora current response read proposal v1. UserRequest is the unchanged original user request. "
        + "The single evidence object describes speech.read_once, not an executable tool. "
        + "Propose it only when the user asks to hear the current answer aloud once. "
        + "Do not propose reading other sources, history, prompts, questions or approvals, arbitrary text, "
        + "preference changes, protected-call exceptions or any other action. "
        + "Return only one JSON object with exactly one property target: copy the supplied opaque target string "
        + "for that operation, otherwise null. No text, IDs, extra fields or Markdown. "
        + "Never execute anything or claim approval or playback. No tools, history, memory, attachments, hosted model or network.";

    private sealed class ResponseReadProposal(HostRequest request, string text, ResponseReadInput input,
        string reference, DateTimeOffset expiresAt, Func<bool> current, ActivityContext cause)
    {
        internal HostRequest Request { get; } = request;
        internal string Text { get; } = text;
        internal ResponseReadInput Input { get; } = input;
        internal string Reference { get; } = reference;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal Func<bool> Current { get; } = current;
        internal ActivityContext Cause { get; } = cause;
        internal bool PlanningStarted { get; set; }
        internal bool Ready { get; set; }
    }

    private IModelContextRunner? responseReadPlanner;
    private TimeProvider responseReadTime = TimeProvider.System;
    private ResponseReadProposal? responseReadProposal;
    private CancellationTokenSource? responseReadPlanCancellation;
    private Task responseReadPlanQuiescence = Task.CompletedTask;
    private long responseReadOperationRevision;

    public AsyncCommand PlanCurrentResponseReadCommand { get; }
    public AsyncCommand ConfirmCurrentResponseReadCommand { get; }
    public AsyncCommand CancelCurrentResponseReadCommand { get; }
    public bool HasResponseReadProposal => responseReadProposal is not null;
    public bool CanPlanCurrentResponseRead => responseReadProposal is { PlanningStarted: false } review && review.Current();
    public bool CanConfirmCurrentResponseRead => responseReadProposal is { Ready: true } review && review.Current()
        && responseReadPlanCancellation is null && responseReadPlanQuiescence.IsCompleted;
    public string ResponseReadProposalReview => responseReadProposal is { } review
        ? (review.Ready
            ? "Read the answer captured when this request arrived, once? The local planner has joined. "
                + "Confirm separately; normal speech, call, privacy, output and original-channel gates still apply."
            : review.PlanningStarted ? "Interpreting locally. No speech is authorized. Cancellation waits for the planner to join."
            : "Interpret this request locally to propose reading the captured current answer? "
                + LocalContextSessionTarget.FreshSessionDisclosure
                + " Only the original request and an opaque host target reference go to the local model, not the answer.")
            + "\nThe complete answer remains above. This review expires after two minutes; replacement, even with identical text, invalidates it."
            + "\nOriginal request: " + review.Text
        : string.Empty;

    public void BindResponseReadPlanner(IModelContextRunner planner, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(time);
        RetireResponseReadProposal();
        responseReadPlanner = planner;
        responseReadTime = time;
    }

    public Func<Task> CaptureResponseReadProposalDecision(bool confirm)
    {
        var review = responseReadProposal;
        return () => RunResponseReadProposalDecision(review, confirm ? "confirm" : "cancel");
    }

    private void NotifyResponseReadProposal()
    {
        OnPropertyChanged(nameof(HasResponseReadProposal));
        OnPropertyChanged(nameof(CanPlanCurrentResponseRead));
        OnPropertyChanged(nameof(CanConfirmCurrentResponseRead));
        OnPropertyChanged(nameof(ResponseReadProposalReview));
        PlanCurrentResponseReadCommand.NotifyCanExecuteChanged();
        ConfirmCurrentResponseReadCommand.NotifyCanExecuteChanged();
    }

    private void RetireResponseReadProposal()
    {
        if (responseReadProposal is not null) { LastResponseReadOutcome = SpeechReadOutcome.Denied; }
        responseReadProposal = null;
        responseReadPlanCancellation?.Cancel();
        NotifyResponseReadProposal();
    }

    private async Task CloseResponseReadProposal()
    {
        if (responseReadProposal is null && responseReadPlanCancellation is null && responseReadPlanQuiescence.IsCompleted) { return; }
        RetireResponseReadProposal();
#pragma warning disable VSTHRD003 // Owned planning retains its lease until disposal and physical model quiescence finish.
        await responseReadPlanQuiescence;
#pragma warning restore VSTHRD003
        LastResponseReadOutcome = SpeechReadOutcome.Cancelled;
        ReportResponseRead("Read proposal cancelled and local planning joined. Full visual answer retained; no replay.");
    }

    private string ResponseReadProposalBody(string text) => commandRouter.IsActivationPrefixed(text, AssistantName)
        ? text[AssistantName.Length..].TrimStart(' ', ',', ':', '-', '\u2014') : text.Trim();

    private async Task<bool> TryHandleResponseReadProposal(string text, SecurityAuditInitiator initiator,
        ResponseReadInput? captured)
    {
        var body = ResponseReadProposalBody(text);
        var decision = body.ToLowerInvariant() switch
        {
            "interpret current response read" => "plan",
            "confirm current response read" => "confirm",
            "cancel current response read" => "cancel",
            _ => null,
        };
        // This only reserves a request family. The tool-free planner, not this lexical route, proposes the operation.
        var candidate = decision is not null || ResponseReadCommand.Parse(text, AssistantName) is null
            && (body.Contains("aloud", StringComparison.OrdinalIgnoreCase)
                || body.Contains("out loud", StringComparison.OrdinalIgnoreCase)
                || body.Contains("this answer", StringComparison.OrdinalIgnoreCase)
                || body.Contains("current answer", StringComparison.OrdinalIgnoreCase)
                || body.Contains("this response", StringComparison.OrdinalIgnoreCase)
                || body.Contains("current response", StringComparison.OrdinalIgnoreCase))
            && (body.Contains("read", StringComparison.OrdinalIgnoreCase) || body.Contains("speak", StringComparison.OrdinalIgnoreCase));
        if (!candidate) { return false; }
        if (Encoding.UTF8.GetByteCount(text) > 4096 || text.Any(char.IsControl))
        {
            ReportResponseRead("Read proposal refused: use one bounded original request without control characters. No source was replaced.");
            return true;
        }
        var origin = OriginalOrigin(initiator);
        if (!isAssistantNameAvailable || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(text))
        {
            ReportResponseRead("Read proposal refused: original typed input or current-name activated voice is required. No replay.");
            return true;
        }
        if (decision is not null)
        {
            await RunResponseReadProposalDecision(responseReadProposal, decision);
            return true;
        }
        if (responseReadPlanCancellation is not null)
        {
            ReportResponseRead("The original local read planner is still running or joining. Cancel it before a fresh request; no replacement was selected.");
            return true;
        }
        RetireResponseReadProposal();
        var input = captured ?? CaptureResponseReadInput(initiator);
        var source = Volatile.Read(ref ordinaryResponse);
        var host = CaptureModelActionReviewHost();
        if (responseReadPlanner is null || source?.Request is null || input.Target is not { } target
            || target.ResponseId != source.Id || !input.Eligible() || !host() || !CanEnterRetainedAnswerRead
            || IsSpeaking || activeReasoningCancellation is not null || isModelActionDispatchActive || isModelApprovalPromptActive)
        {
            LastResponseReadOutcome = SpeechReadOutcome.Denied;
            ReportResponseRead("Current-answer proposal unavailable: an eligible exact ordinary answer, idle local planner and unchanged original-user speech admission are required. No decision was answered, source replaced or playback queued.");
            return true;
        }
        var current = HostActivity.RequireCurrent();
        if (current.Request.InvocationId is not null)
        {
            ReportResponseRead("Read proposal refused: model/tool invocations cannot supply original-user authority.");
            return true;
        }
        var now = responseReadTime.GetUtcNow();
        var time = responseReadTime;
        bool Current() => time.GetUtcNow() >= now && time.GetUtcNow() < now.AddMinutes(2)
            && host() && input.Eligible() && CanEnterRetainedAnswerRead;
        responseReadProposal = new(current.Request, text, input, Guid.NewGuid().ToString("N"), now.AddMinutes(2), Current,
            current.Activity!.Context);
        LastResponseReadOutcome = null;
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
        NotifyResponseReadProposal();
        ReportResponseRead("Review local interpretation separately. No speech was requested and the captured answer is unchanged.");
        WindowActionRequested?.Invoke(this, WindowAction.Show);
        return true;
    }

    private Task RunResponseReadProposalDecision(ResponseReadProposal? review, string operation)
    {
        var current = HostActivity.Current;
        if (HostActivity.HasScope && (current is null || current.Activity!.IsStopped
            || current.Outcome != HostOperationOutcome.Unknown || current.Request.InvocationId is not null
            || current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)))
        {
            ReportResponseRead("Read proposal decision refused: live original-user input is required.");
            return Task.CompletedTask;
        }
        return HostRequestRunner.RunAsync(current?.Request.Origin ?? RequestOrigin.LocalUi,
            () => ExecuteResponseReadProposalDecision(review, operation));
    }

    private async Task ExecuteResponseReadProposalDecision(ResponseReadProposal? review, string operation)
    {
        if (review is null || !ReferenceEquals(review, responseReadProposal))
        {
            ReportResponseRead("That exact read review is no longer available. No replacement was selected.");
            return;
        }
        if (string.Equals(operation, "cancel", StringComparison.Ordinal))
        {
            RetireResponseReadProposal();
#pragma warning disable VSTHRD003 // Cancellation cannot release the read planner until its admitted lease has physically joined.
            await responseReadPlanQuiescence;
#pragma warning restore VSTHRD003
            LastResponseReadOutcome = SpeechReadOutcome.Cancelled;
            ReportResponseRead("Read proposal cancelled and local planning joined. Full visual answer retained; no replay.");
            return;
        }
        if (!review.Current() || IsSpeaking || responseReadCancellation is not null)
        {
            RetireResponseReadProposal();
            LastResponseReadOutcome = SpeechReadOutcome.Denied;
            ReportResponseRead("Read review expired or its source, original channel or speech admission changed. No playback or replay.");
            return;
        }
        if (string.Equals(operation, "plan", StringComparison.Ordinal))
        {
            if (review.PlanningStarted) { return; }
            review.PlanningStarted = true;
            var remaining = review.ExpiresAt - responseReadTime.GetUtcNow();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromTicks(Math.Max(0, remaining.Ticks)), responseReadTime);
            responseReadPlanCancellation = cancellation;
            IsLocalTaskCancellable = true;
            var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            responseReadPlanQuiescence = joined.Task;
            NotifyResponseReadProposal();
            using var activity = HostActivity.BeginRoot(HostRequest.Create(review.Request.Origin),
                HostActivityLayer.Application, HostOperation.Request, [new(review.Cause)]);
            bool Current() => ReferenceEquals(review, responseReadProposal) && review.Current() && !cancellation.IsCancellationRequested;
            try
            {
                var request = HostRequest.Create(review.Request.Origin);
                var now = responseReadTime.GetUtcNow();
                var context = new ModelContextEnvelope(request, ResponseReadProposalPolicy, review.Text,
                    [new(new(Guid.NewGuid()), request, new(1),
                        JsonSerializer.Serialize(new { action = "speech.read_once", target = review.Reference }),
                        ModelEvidenceDisclosure.LocalOnly)], now, review.ExpiresAt, ModelToolMode.None);
                context.RequireCompleteInputLimit();
                var candidate = await responseReadPlanner!.AdmitAsync(
                    LocalContextSessionTarget.ReviewedFresh(activity.Request, request), Current, cancellation.Token);
                var result = candidate.Rejection!;
                if (candidate.Turn is { } turn)
                {
                    await using (turn.ConfigureAwait(false))
                    { result = await turn.RunAsync(context, cancellation.Token); }
                }
                if (!Current() || result.Answer is not { } answer || !AcceptResponseReadProposal(answer, review.Reference))
                {
                    var cancelled = cancellation.IsCancellationRequested;
                    RetireResponseReadProposal();
                    LastResponseReadOutcome = cancelled ? SpeechReadOutcome.Cancelled : SpeechReadOutcome.Denied;
                    ReportResponseRead("No current-answer read proposal was admitted. Planner joined; full visual answer retained. No fallback or replay.");
                    activity.Complete(HostOperationOutcome.Cancelled);
                    return;
                }
                review.Ready = true;
                ReportResponseRead("Local interpretation joined. Separately confirm reading the original captured answer once; nothing has played.");
                activity.Complete(HostOperationOutcome.Completed);
            }
            catch (OperationCanceledException)
            {
                RetireResponseReadProposal();
                LastResponseReadOutcome = SpeechReadOutcome.Cancelled;
                ReportResponseRead("Local read planning cancelled and joined. Full visual answer retained; no replay.");
                activity.Complete(HostOperationOutcome.Cancelled);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                or JsonException or ArgumentException)
            {
                RetireResponseReadProposal();
                ResponseReadFailed(logger, exception.GetType().Name);
                LastResponseReadOutcome = SpeechReadOutcome.Unknown;
                ReportResponseRead("Local read planning or required evidence is unconfirmed. Full visual answer retained; no playback, retry or replay.");
                activity.Complete(HostOperationOutcome.Unknown);
            }
            finally
            {
                responseReadPlanCancellation = null;
                IsLocalTaskCancellable = false;
                joined.TrySetResult();
                NotifyResponseReadProposal();
            }
            return;
        }
        if (!review.Ready || responseReadPlanCancellation is not null || !responseReadPlanQuiescence.IsCompleted)
        {
            ReportResponseRead("Playback requires a separate decision after local interpretation and its physical join.");
            return;
        }
        responseReadProposal = null;
        NotifyResponseReadProposal();
        using var playback = HostActivity.BeginRoot(HostRequest.Create(review.Request.Origin),
            HostActivityLayer.Application, HostOperation.Presentation, [new(review.Cause)]);
        if (review.Current())
        {
            var outcome = await speechReadOnce.ExecuteAsync(review.Input.Target!.Value, review.Input.Initiator, CancellationToken.None);
            playback.Complete(ResponseReadActivityOutcome(outcome));
        }
        else
        {
            LastResponseReadOutcome = SpeechReadOutcome.Denied;
            ReportResponseRead("Original read source retired before playback. No replacement, retry or replay.");
            playback.Complete(HostOperationOutcome.Cancelled);
        }
    }

    private static bool AcceptResponseReadProposal(string answer, string reference)
    {
        if (Encoding.UTF8.GetByteCount(answer) > 1024) { return false; }
        using var document = JsonDocument.Parse(answer, new JsonDocumentOptions { MaxDepth = 2 });
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1
            && root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String
            && string.Equals(target.GetString(), reference, StringComparison.Ordinal);
    }
}
