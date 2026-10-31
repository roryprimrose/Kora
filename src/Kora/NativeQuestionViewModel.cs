using System.Diagnostics;
using System.Text.Json;

using Kora.Application.Infrastructure;
using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class NativeQuestionViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions ReviewFormat = new() { WriteIndented = true };
    private readonly HostQuestionService questions;
    private readonly HostAuthorizationService authorization;
    private readonly HostQuestionReviewService reviews;
    private readonly Func<bool> canInteract;
    private readonly TimeProvider time;
    private readonly ILogger<NativeQuestionViewModel> logger;
    private readonly ActivityLink[] links;
    private readonly Func<HostQuestionKey, Task<HostInteractionDecision>>? cancel;
    private readonly bool dismissible;
    private readonly bool explainClipboardRead;
    // Terminal state and its receipt share one boundary. No I/O, gates or notifications run under it.
    private readonly Lock stateGate = new();
    private readonly TaskCompletionSource<HostInteractionDecision> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? pendingOperation;
    private HostQuestionRecord question;
    private HostQuestionKey? reviewedKey;
    private HostQuestionKey? displayedReviewKey;
    private bool busy;
    private bool closed;
    private bool cleared;
    private bool withdrawn;
    private long presentationRevision;
    private string status = "Choose an answer. Editing is local; Save draft and Submit are explicit.";
    private string reviewText = string.Empty;
    private string mcpReviewText = string.Empty;
    private PendingOperationExplanation? policyExplanation;
    private QuestionAnswer answer;
    private HostQuestionVoiceReplyService? voiceReplies;
    private Func<bool> ownsVoiceTarget = static () => false;
    private Func<bool> visibleVoicePresenter = static () => false;
    private HostQuestionKey? voiceTarget;
    private int voicePresenterCount;
    private bool conversationalReplyAttempted;
    internal void BindMcpReview(Kora.Application.Mcp.McpReview review)
    {
        if (question.Proposal is null || !review.IsBoundTo(question.Proposal))
        { throw new InvalidOperationException("The native MCP review does not match the exact operation."); }
        mcpReviewText = review.NativeText;
    }
    internal string VoiceReplyStatus => voiceReplies?.Status ?? "Question voice reply unavailable. Use native answer controls.";
    internal bool IsVoiceReplyActive => voiceReplies?.IsActive == true;
    internal bool CanAnswerByVoice => voiceReplies is not null && IsEditable && !IsApproval
        && ownsVoiceTarget() && voicePresenterCount == 1 && visibleVoicePresenter() && !IsVoiceReplyActive;

    internal void BindVoiceReplies(HostQuestionVoiceReplyService? replies, Func<bool> ownsTarget)
    {
        voiceReplies = replies;
        ownsVoiceTarget = ownsTarget;
        if (replies is not null) { replies.Changed += OnVoiceReplyChanged; }
    }

    internal void BindVisibleVoicePresenter(Func<bool> visible)
    {
        StopVoiceReply();
        voicePresenterCount++;
        visibleVoicePresenter = visible;
    }

    internal Task AnswerByVoiceAsync(bool afterPlayback = false)
    {
        if (!CanAnswerByVoice) { StopVoiceReply(); return Task.CompletedTask; }
        voiceTarget = Key;
        var exact = Question;
        return voiceReplies!.OpenAsync(exact,
            () => ownsVoiceTarget() && voicePresenterCount == 1 && visibleVoicePresenter() && IsEditable && Key == exact.Key,
            AcceptVoiceDecision, afterPlayback, CancellationToken.None);
    }

    internal Task TryConversationalReplyAsync()
    {
        if (conversationalReplyAttempted) { return Task.CompletedTask; }
        if (!CanAnswerByVoice) { return Task.CompletedTask; }
        conversationalReplyAttempted = true;
        voiceTarget = Key;
        var exact = Question;
        return voiceReplies!.OpenConversationalAsync(exact,
            () => ownsVoiceTarget() && voicePresenterCount == 1 && visibleVoicePresenter() && IsEditable
                && ReferenceEquals(Question, exact),
            AcceptVoiceDecision, CancellationToken.None);
    }

    internal void StopVoiceReply()
    {
        if (voiceTarget is not null)
        {
            voiceTarget = null;
            voiceReplies?.Retire("Reply cancelled or presenter changed. Microphone closed; question remains answerable.");
        }
    }

    private void AcceptVoiceDecision(HostInteractionDecision decision)
    {
        lock (stateGate)
        {
            if (closed || cleared || decision.Outcome != HostInteractionOutcome.Answered
                || decision.Question is not { } accepted || accepted.Key != question.Key.Next()
                || accepted.SessionGeneration != question.SessionGeneration) { return; }
            question = accepted;
            closed = true;
            ClearAnswer();
            status = "Exact question answered by local voice. Microphone closed.";
            completion.TrySetResult(decision);
        }
        Notify();
    }

    private void OnVoiceReplyChanged(object? sender, EventArgs args)
    {
        void Update()
        {
            OnPropertyChanged(nameof(VoiceReplyStatus));
            OnPropertyChanged(nameof(IsVoiceReplyActive));
            OnPropertyChanged(nameof(CanAnswerByVoice));
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { Update(); }
        else { Avalonia.Threading.Dispatcher.UIThread.Post(Update); }
    }

    internal NativeQuestionViewModel(HostQuestionRecord question, HostQuestionService questions,
        HostAuthorizationService authorization, HostQuestionReviewService reviews, Func<bool> canInteract,
        TimeProvider time, ILogger<NativeQuestionViewModel> logger,
        Func<HostQuestionKey, Task<HostInteractionDecision>>? cancel = null, bool dismissible = false,
        bool explainClipboardRead = false)
    {
        this.question = question;
        this.questions = questions;
        this.authorization = authorization;
        this.reviews = reviews;
        this.canInteract = canInteract;
        this.time = time;
        this.logger = logger;
        this.cancel = cancel;
        this.dismissible = dismissible;
        this.explainClipboardRead = explainClipboardRead;
        links = Activity.Current is { } activity ? [new(activity.Context)] : [];
        answer = question.Draft ?? EmptyAnswer();
    }

    internal HostQuestionRecord Question { get { lock (stateGate) { return question; } } }
    internal HostQuestionKey Key { get { lock (stateGate) { return question.Key; } } }
    internal QuestionAnswer Answer { get { lock (stateGate) { return answer; } } }
    internal string Status { get { lock (stateGate) { return status; } } }
    internal string ReviewText { get { lock (stateGate) { return reviewText; } } }
    internal bool SupportsPolicyExplanation => explainClipboardRead;
    internal bool HasPolicyExplanation { get { lock (stateGate) { return policyExplanation is not null; } } }
    internal string PolicyExplanationText
    {
        get
        {
            lock (stateGate)
            {
                if (policyExplanation is not { } observed) { return string.Empty; }
                var grant = observed.ApplicableGrant is { } exact
                    ? $"Matching exact {exact.Scope} grant {exact.Id.Value:D} (revision {exact.Revision.Value}) was observed; "
                        + "it has not been reserved or consumed."
                    : "No applicable exact grant was observed. This read still needs native Once, This session or Always approval.";
                return "Read snapshot only: clipboard.read - current plain-text clipboard to volatile native preview.\n"
                    + $"Current session level at observation: {observed.SessionPolicy.DisplayName}.\n"
                    + grant + "\nThis path requires exact clipboard-read permission independently of the session level. "
                    + "It does not permit model use, writing, speech or history.\n"
                    + "Current effect authority: NOT supplied by this explanation. No clipboard contents were read. "
                    + "Return to the same approval rechecks these facts; approval and execution still use their normal fresh checks. "
                    + "Native controls only; no spoken approval or sensitive disclosure is added.";
            }
        }
    }
    internal bool HasPresentation { get { lock (stateGate) { return !cleared; } } }
    internal bool HasPendingOperation { get { lock (stateGate) { return busy; } } }
    internal bool IsWithdrawn { get { lock (stateGate) { return withdrawn; } } }
    internal bool IsApproval { get { lock (stateGate) { return question.Proposal is not null; } } }
    internal bool IsEditable
    {
        get
        {
            var eligible = canInteract();
            var now = time.GetUtcNow();
            lock (stateGate) { return eligible && IsEditableAt(now); }
        }
    }
    internal bool CanSubmit
    {
        get
        {
            if (!IsEditable) { return false; }
            lock (stateGate)
            {
                return !closed && !cleared && !busy && policyExplanation is null && question.Spec.Accepts(answer, submitting: true)
                    && (question.Proposal is null || (reviewedKey == question.Key && displayedReviewKey == question.Key));
            }
        }
    }
    internal bool CanSaveDraft
    {
        get
        {
            if (!IsEditable) { return false; }
            lock (stateGate) { return !closed && !cleared && !busy && question.Spec.Accepts(answer, submitting: false); }
        }
    }
#pragma warning disable VSTHRD003 // Exposes the owned interaction receipt; asynchronous observers leave the dispatcher available for completion.
    internal Task<HostInteractionDecision> Completion => completion.Task;
#pragma warning restore VSTHRD003
    internal Task OperationQuiescence
    {
        get { lock (stateGate) { return pendingOperation?.Task ?? Task.CompletedTask; } }
    }

    internal void Edit(QuestionAnswer value)
    {
        StopVoiceReply();
        if (!IsEditable)
        {
            RefreshEligibility();
            return;
        }
        lock (stateGate)
        {
            if (closed || cleared || busy) { return; }
            answer = value;
            status = question.Spec.Accepts(value, submitting: false)
                ? "Unsubmitted local edit. Save draft or Submit explicitly."
                : "Invalid answer: use the displayed choice and text bounds; nothing was shortened or saved.";
        }
        Notify();
    }

    internal Task SaveDraftAsync()
    {
        HostQuestionKey target;
        QuestionAnswer value;
        lock (stateGate) { target = question.Key; value = answer; }
        return DecideAsync(target, () => questions.DraftAsync(target, value, RequestOrigin.LocalUi, CancellationToken.None));
    }

    internal Task SubmitAsync()
    {
        HostQuestionKey target;
        QuestionAnswer value;
        bool approval;
        bool needsReview;
        lock (stateGate)
        {
            if (closed || cleared) { return Task.CompletedTask; }
            target = question.Key;
            value = answer;
            approval = question.Proposal is not null;
            needsReview = approval && (policyExplanation is not null || reviewedKey != target || displayedReviewKey != target);
            if (needsReview) { status = "Review the exact current operation before approval. Reading does not approve or use it."; }
        }
        if (needsReview)
        {
            Notify();
            return Task.CompletedTask;
        }
        return DecideAsync(target, () => approval
            ? authorization.ApproveAsync(target, value, RequestOrigin.LocalUi, CancellationToken.None)
            : questions.SubmitAsync(target, value, RequestOrigin.LocalUi, CancellationToken.None));
    }

    internal Task CancelAsync()
    {
        var target = Key;
        return DecideAsync(target, () => cancel is null
            ? questions.CancelAsync(target, CancellationToken.None) : new(cancel(target)));
    }

    internal void AcceptCommittedCancellation(HostTaskObservation observation)
    {
        if (!canInteract()) { return; }
        lock (stateGate)
        {
            if (closed || observation.Task.Request != question.Key.Request
                || observation.Task.State != HostTaskState.Cancelled || observation.Question is not { } cancelled
                || cancelled.Key != question.Key.Next() || cancelled.Status != QuestionStatus.Cancelled
                || observation.Generation != question.SessionGeneration || cancelled.SessionGeneration != question.SessionGeneration)
            {
                return;
            }
            question = cancelled;
            closed = true;
            ClearAnswer();
            status = "Exact task and question cancelled atomically before dispatch.";
            completion.TrySetResult(new(HostInteractionOutcome.Cancelled, "pre-dispatch-work-cancelled", cancelled));
        }
        Notify();
    }

    internal Task ReviewAsync()
    {
        var target = Key;
        return DecideAsync(target, () => reviews.ReviewAsync(target, CancellationToken.None), reviewing: true);
    }

    internal Task RefreshTargetAsync()
    {
        var target = Key;
        return DecideAsync(target, () => reviews.ReviewAsync(target, CancellationToken.None), checking: true);
    }

    internal Task ExplainPolicyAsync()
    {
        if (!explainClipboardRead) { return Task.CompletedTask; }
        var expected = Question;
        return DecideAsync(expected.Key,
            () => authorization.ExplainPendingClipboardReadAsync(expected, null, CancellationToken.None), explaining: true);
    }

    internal Task ReturnToApprovalAsync()
    {
        var expected = Question;
        PendingOperationExplanation? previous;
        lock (stateGate) { previous = policyExplanation; }
        if (previous is null) { return Task.CompletedTask; }
        return DecideAsync(expected.Key,
            () => authorization.ExplainPendingClipboardReadAsync(expected, previous, CancellationToken.None), returning: true);
    }

    internal void ReportOutcome(string message)
    {
        RefreshEligibility();
        lock (stateGate)
        {
            if (cleared) { return; }
            status = message;
        }
        Notify();
    }

    private async Task DecideAsync(HostQuestionKey target, Func<ValueTask<HostInteractionDecision>> decide,
        bool reviewing = false, bool checking = false, bool explaining = false, bool returning = false)
    {
        StopVoiceReply();
        if (!IsEditable) { RefreshEligibility(); return; }
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (stateGate)
        {
            if (closed || cleared || busy || question.Key != target) { return; }
            busy = true;
            pendingOperation = operation;
        }
        Notify();
        using var activity = HostActivity.BeginRoot(target.Request, HostActivityLayer.Desktop, HostOperation.Presentation, links);
        try
        {
            var decision = await decide();
            if (decision.Question is { } next && (next.Key.Request != target.Request
                || next.Key.QuestionId != target.QuestionId))
            {
                throw new InvalidDataException("The host returned a foreign question; no presentation was retargeted.");
            }
            var eligible = canInteract();
            var review = reviewing && decision.Outcome == HostInteractionOutcome.Presented
                ? JsonSerializer.Serialize(decision.Question, ReviewFormat) : string.Empty;
            bool interrupted;
            lock (stateGate)
            {
                interrupted = !eligible || closed;
                if (interrupted)
                {
                    CloseState("Interaction closed during the commit. No late result is presented or dispatched.");
                }
                else
                {
                    status = decision.Reason;
                    if ((explaining || returning) && decision.Outcome == HostInteractionOutcome.Presented)
                    {
                        var observed = decision.Explanation
                            ?? throw new InvalidDataException("The policy observation is unavailable.");
                        if (observed.Question != target || observed.Proposal != question.Proposal)
                        { throw new InvalidDataException("The policy observation changed the original operation."); }
                        policyExplanation = returning ? null : observed;
                        status = returning ? "Same pending approval revalidated. No approval or grant use occurred."
                            : "Policy snapshot read. Return to this same approval or explicitly cancel; nothing has run.";
                    }
                    else if ((reviewing || checking) && decision.Outcome == HostInteractionOutcome.Presented)
                    {
                        var exact = decision.Question ?? throw new InvalidDataException("The exact review has no question.");
                        if (exact.Key != target || exact.Proposal != question.Proposal)
                        {
                            throw new InvalidDataException("The review no longer matches the immutable displayed target.");
                        }
                        if (reviewing)
                        {
                            reviewText = cleared ? string.Empty : mcpReviewText.Length == 0
                                ? review : mcpReviewText + "\n\nHost record:\n" + review;
                            reviewedKey = cleared ? null : target;
                            displayedReviewKey = null;
                            status = "Exact host record reviewed. Operation source bytes are not supplied by this contract. Review is not approval, use or execution.";
                        }
                    }
                    else if (decision.Outcome == HostInteractionOutcome.Drafted)
                    {
                        question = decision.Question!;
                        ClearAnswer();
                        answer = cleared ? EmptyAnswer() : question.Draft!;
                    }
                    else
                    {
                        policyExplanation = null;
                        closed = true;
                        completion.TrySetResult(decision);
                    }
                }
            }
            if (interrupted)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            activity.Complete(decision.Outcome is HostInteractionOutcome.Denied or HostInteractionOutcome.Conflict or HostInteractionOutcome.Expired
                ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            lock (stateGate) { busy = false; }
            try { Notify(); }
            finally { operation.TrySetResult(); }
        }
    }

    internal void RefreshEligibility()
    {
        var eligible = canInteract();
        var now = time.GetUtcNow();
        lock (stateGate)
        {
            if (!eligible || (!closed && question.ExpiresAt <= now))
            {
                CloseState("Expired or privacy/ownership unavailable. Start a fresh host interaction; this target cannot be answered.");
            }
        }
        Notify();
    }

    internal void CompleteReviewPresentation(string exactDisplayedText)
    {
        lock (stateGate)
        {
            if (reviewedKey != question.Key || reviewText.Length == 0 || closed) { return; }
            if (!string.Equals(exactDisplayedText, reviewText, StringComparison.Ordinal))
            {
                displayedReviewKey = null;
                status = "Exact review could not be displayed completely. Approval remains disabled; no missing content is authorized.";
            }
            else if (displayedReviewKey != question.Key) { displayedReviewKey = question.Key; }
            else { return; }
        }
        Notify();
    }

    internal void Close(string reason)
    {
        StopVoiceReply();
        if (voiceReplies is not null) { voiceReplies.Changed -= OnVoiceReplyChanged; }
        lock (stateGate) { CloseState(reason); }
        Notify();
    }

    private void CloseState(string reason)
    {
        // Withdrawal must still clear private presentation after an earlier terminal receipt.
        withdrawn = true;
        closed = true;
        cleared = true;
        presentationRevision++;
        ClearAnswer();
        status = reason;
        completion.TrySetResult(new(HostInteractionOutcome.Denied, "native-presentation-closed"));
    }

    internal void Dismiss()
    {
        StopVoiceReply();
        voicePresenterCount = Math.Max(0, voicePresenterCount - 1);
        visibleVoicePresenter = static () => false;
        lock (stateGate)
        {
            if (!dismissible || closed)
            {
                CloseState("Native presentation closed. No answer, approval or execution is implied by window closure.");
            }
            else
            {
                cleared = true;
                presentationRevision++;
                ClearAnswer();
                status = "Presentation dismissed. Return to the current question before its original expiry, or explicitly cancel it.";
            }
        }
        Notify();
    }

    internal async Task RestorePresentationAsync()
    {
        RefreshEligibility();
        HostQuestionRecord target;
        long revision;
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (stateGate)
        {
            if (!dismissible || closed || busy || !cleared) { return; }
            target = question;
            revision = presentationRevision;
            busy = true;
            pendingOperation = operation;
        }
        using var activity = HostActivity.BeginRoot(target.Key.Request, HostActivityLayer.Desktop, HostOperation.Presentation, links);
        try
        {
            var decision = await reviews.ReviewAsync(target.Key, CancellationToken.None);
            var eligible = canInteract();
            var now = time.GetUtcNow();
            bool restored;
            lock (stateGate)
            {
                restored = false;
                if (decision.Outcome != HostInteractionOutcome.Presented || decision.Question is not { } current
                    || current.Key != target.Key || current.SessionGeneration != target.SessionGeneration
                    || current.ExpiresAt != target.ExpiresAt || current.Proposal is not null
                    || closed || !eligible || current.ExpiresAt <= now)
                {
                    CloseState("Current question unavailable. Inspect retained status and start a fresh request; nothing was replayed.");
                }
                else if (revision == presentationRevision)
                {
                    question = current;
                    answer = current.Draft ?? EmptyAnswer();
                    cleared = false;
                    status = "Original current-run question restored. Only a committed draft is restored; Submit and Cancel remain explicit.";
                    restored = true;
                }
            }
            activity.Complete(restored ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            lock (stateGate) { busy = false; }
            try { Notify(); }
            finally { operation.TrySetResult(); }
        }
    }

    internal void Fail(Exception exception)
    {
        HostQuestionKey target;
        lock (stateGate)
        {
            target = question.Key;
            completion.TrySetException(exception);
            CloseState("Interaction/storage/audit failed. No successful answer, approval or use is claimed. Inspect retained status and start a fresh request; uncertain work is not replayed.");
        }
        Notify();
        try { QuestionFailure(logger, target.QuestionId.Value, target.Revision.Value, exception.GetType().FullName); }
        catch (Exception evidenceFailure) when (evidenceFailure is IOException or InvalidOperationException)
        {
            lock (stateGate) { status += " Diagnostic evidence is also unavailable."; }
            Notify();
        }
    }

    private bool IsEditableAt(DateTimeOffset now) => !closed && !cleared && !busy
        && question.Status == QuestionStatus.Pending && question.ExpiresAt > now;

    private void ClearAnswer()
    {
        answer = EmptyAnswer();
        reviewText = string.Empty;
        mcpReviewText = string.Empty;
        policyExplanation = null;
        reviewedKey = null;
        displayedReviewKey = null;
    }

    private QuestionAnswer EmptyAnswer() => new([], question.Spec.Kind == QuestionKind.Text ? string.Empty : null);

    private void Notify()
    {
        OnPropertyChanged(nameof(Question));
        OnPropertyChanged(nameof(Answer));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ReviewText));
        OnPropertyChanged(nameof(PolicyExplanationText));
        OnPropertyChanged(nameof(HasPolicyExplanation));
        OnPropertyChanged(nameof(HasPresentation));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(CanAnswerByVoice));
        OnPropertyChanged(nameof(IsVoiceReplyActive));
        OnPropertyChanged(nameof(VoiceReplyStatus));
    }
}
