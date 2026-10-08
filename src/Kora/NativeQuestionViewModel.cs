using System.Diagnostics;
using System.Text.Json;

using Kora.Application.Infrastructure;
using Kora.Application.Interaction;
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
    private readonly TaskCompletionSource<HostInteractionDecision> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HostQuestionRecord question;
    private HostQuestionKey? reviewedKey;
    private HostQuestionKey? displayedReviewKey;
    private bool busy;
    private bool closed;
    private bool cleared;
    private string status = "Choose an answer. Editing is local; Save draft and Submit are explicit.";
    private string reviewText = string.Empty;
    private QuestionAnswer answer;

    internal NativeQuestionViewModel(HostQuestionRecord question, HostQuestionService questions,
        HostAuthorizationService authorization, HostQuestionReviewService reviews, Func<bool> canInteract,
        TimeProvider time, ILogger<NativeQuestionViewModel> logger,
        Func<HostQuestionKey, Task<HostInteractionDecision>>? cancel = null)
    {
        this.question = question;
        this.questions = questions;
        this.authorization = authorization;
        this.reviews = reviews;
        this.canInteract = canInteract;
        this.time = time;
        this.logger = logger;
        this.cancel = cancel;
        links = Activity.Current is { } activity ? [new(activity.Context)] : [];
        answer = question.Draft ?? EmptyAnswer();
    }

    internal HostQuestionRecord Question => question;
    internal HostQuestionKey Key => question.Key;
    internal QuestionAnswer Answer => answer;
    internal string Status => status;
    internal string ReviewText => reviewText;
    internal bool HasPresentation => !cleared;
    internal bool IsApproval => question.Proposal is not null;
    internal bool IsEditable => !closed && !busy && question.Status == QuestionStatus.Pending
        && question.ExpiresAt > time.GetUtcNow() && canInteract();
    internal bool CanSubmit => IsEditable && question.Spec.Accepts(answer, submitting: true)
        && (!IsApproval || (reviewedKey == Key && displayedReviewKey == Key));
    internal bool CanSaveDraft => IsEditable && question.Spec.Accepts(answer, submitting: false);
    internal Task<HostInteractionDecision> Completion => completion.Task;

    internal void Edit(QuestionAnswer value)
    {
        if (!IsEditable)
        {
            RefreshEligibility();
            return;
        }
        answer = value;
        status = question.Spec.Accepts(value, submitting: false)
            ? "Unsubmitted local edit. Save draft or Submit explicitly."
            : "Invalid answer: use the displayed choice and text bounds; nothing was shortened or saved.";
        Notify();
    }

    internal Task SaveDraftAsync() => DecideAsync(Key, () =>
        questions.DraftAsync(Key, answer, RequestOrigin.LocalUi, CancellationToken.None));

    internal Task SubmitAsync()
    {
        if (IsApproval && (reviewedKey != Key || displayedReviewKey != Key))
        {
            status = "Review the exact current operation before approval. Reading does not approve or use it.";
            Notify();
            return Task.CompletedTask;
        }
        return DecideAsync(Key, () => IsApproval
            ? authorization.ApproveAsync(Key, answer, RequestOrigin.LocalUi, CancellationToken.None)
            : questions.SubmitAsync(Key, answer, RequestOrigin.LocalUi, CancellationToken.None));
    }

    internal Task CancelAsync() => DecideAsync(Key, () => cancel is null
        ? questions.CancelAsync(Key, CancellationToken.None) : new(cancel(Key)));

    internal void AcceptCommittedCancellation(HostTaskObservation observation)
    {
        if (closed || !canInteract() || observation.Task.Request != Key.Request
            || observation.Task.State != HostTaskState.Cancelled || observation.Question is not { } cancelled
            || cancelled.Key.QuestionId != Key.QuestionId || cancelled.Status != QuestionStatus.Cancelled)
        {
            return;
        }
        question = cancelled;
        closed = true;
        answer = EmptyAnswer();
        reviewText = string.Empty;
        status = "Exact task and question cancelled atomically before dispatch.";
        completion.TrySetResult(new(HostInteractionOutcome.Cancelled, "pre-dispatch-work-cancelled", cancelled));
        Notify();
    }

    internal Task ReviewAsync() => DecideAsync(Key, () => reviews.ReviewAsync(Key, CancellationToken.None), reviewing: true);

    internal Task RefreshTargetAsync() =>
        DecideAsync(Key, () => reviews.ReviewAsync(Key, CancellationToken.None), checking: true);

    internal void ReportOutcome(string message)
    {
        RefreshEligibility();
        if (cleared) { return; }
        status = message;
        Notify();
    }

    private async Task DecideAsync(HostQuestionKey target, Func<ValueTask<HostInteractionDecision>> decide,
        bool reviewing = false, bool checking = false)
    {
        if (!IsEditable) { RefreshEligibility(); return; }
        busy = true;
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
            if (!canInteract() || closed)
            {
                Close("Interaction closed during the commit. No late result is presented or dispatched.");
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            status = decision.Reason;
            if ((reviewing || checking) && decision.Outcome == HostInteractionOutcome.Presented)
            {
                var exact = decision.Question ?? throw new InvalidDataException("The exact review has no question.");
                if (exact.Key != target || exact.Proposal != question.Proposal)
                {
                    throw new InvalidDataException("The review no longer matches the immutable displayed target.");
                }
                if (reviewing)
                {
                    reviewText = JsonSerializer.Serialize(exact, ReviewFormat);
                    reviewedKey = target;
                    displayedReviewKey = null;
                    status = "Exact host record reviewed. Operation source bytes are not supplied by this contract. Review is not approval, use or execution.";
                }
            }
            else if (decision.Outcome == HostInteractionOutcome.Drafted)
            {
                question = decision.Question!;
                reviewedKey = null;
                displayedReviewKey = null;
                reviewText = string.Empty;
                answer = question.Draft!;
            }
            else
            {
                closed = true;
                completion.TrySetResult(decision);
            }
            activity.Complete(decision.Outcome is HostInteractionOutcome.Denied or HostInteractionOutcome.Conflict or HostInteractionOutcome.Expired
                ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            closed = true;
            status = "Interaction/storage/audit failed. No successful answer, approval or use is claimed. Close and start a fresh review; uncertain work is not replayed.";
            completion.TrySetException(exception);
            activity.Complete(HostOperationOutcome.Failed);
            // Never log free-form text, paths, answers or the review record.
            try { QuestionFailure(logger, target.QuestionId.Value, target.Revision.Value, exception.GetType().FullName); }
            catch (Exception evidenceFailure) when (evidenceFailure is IOException or InvalidOperationException)
            {
                status += " Diagnostic evidence is also unavailable.";
            }
        }
        finally
        {
            busy = false;
            Notify();
        }
    }

    internal void RefreshEligibility()
    {
        if (!canInteract() || (!closed && question.ExpiresAt <= time.GetUtcNow()))
        {
            Close("Expired or privacy/ownership unavailable. Start a fresh host interaction; this target cannot be answered.");
        }
        else { Notify(); }
    }

    internal void CompleteReviewPresentation(string exactDisplayedText)
    {
        if (reviewedKey != Key || reviewText.Length == 0 || closed) { return; }
        if (!string.Equals(exactDisplayedText, reviewText, StringComparison.Ordinal))
        {
            displayedReviewKey = null;
            status = "Exact review could not be displayed completely. Approval remains disabled; no missing content is authorized.";
            Notify();
        }
        else if (displayedReviewKey != Key)
        {
            displayedReviewKey = Key;
            Notify();
        }
    }

    internal void Close(string reason)
    {
        closed = true;
        cleared = true;
        answer = EmptyAnswer();
        reviewText = string.Empty;
        reviewedKey = null;
        displayedReviewKey = null;
        status = reason;
        completion.TrySetResult(new(HostInteractionOutcome.Denied, "native-presentation-closed"));
        Notify();
    }

    private QuestionAnswer EmptyAnswer() => new([], question.Spec.Kind == QuestionKind.Text ? string.Empty : null);

    private void Notify()
    {
        OnPropertyChanged(nameof(Question));
        OnPropertyChanged(nameof(Answer));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ReviewText));
        OnPropertyChanged(nameof(HasPresentation));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanSaveDraft));
    }
}
