using System.Text;
using System.Text.Json;
using Kora.Application.Diagnostics.EvidenceAnswers;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class EvidenceViewModel
{
    private EvidenceAnswerWorkflow? evidenceAnswers;
    private Func<Func<bool>>? captureAnswerAdmission;
    private Func<long>? answerControlRevision;
    private TimeProvider answerTime = TimeProvider.System;
    private EvidenceAnswerReview? answerReview;
    private Func<bool>? answerAdmitted;
    private CancellationTokenSource? answerCancellation;
    private string answerQuestion = string.Empty;
    private string answerStatus = string.Empty;
    private string answerText = string.Empty;
    private string answerCitationText = string.Empty;
    private bool answering;
    private bool answerConfirmed;
    private long answerRevision;

    internal void BindEvidenceAnswers(EvidenceAnswerWorkflow workflow, Func<Func<bool>> captureAdmission,
        Func<long> controlRevision, TimeProvider time)
    {
        evidenceAnswers = workflow;
        captureAnswerAdmission = captureAdmission;
        answerControlRevision = controlRevision;
        answerTime = time;
        NotifyEvidenceAnswer();
    }

    public bool HasEvidenceAnswers => evidenceAnswers is not null;
    public bool CanReviewEvidenceAnswer => HasEvidenceAnswers && CanQuery && !answering
        && currentQuery?.Source == EvidenceSource.Log && page?.Status == EvidencePageStatus.Available;
    public bool CanConfirmEvidenceAnswer => !answering && !answerConfirmed && answerReview is not null;
    public bool IsAnswering => answering;
    public string AnswerQuestion
    {
        get => answerQuestion;
        set
        {
            if (string.Equals(answerQuestion, value, StringComparison.Ordinal)) { return; }
            RetireEvidenceAnswer();
            answerQuestion = value;
            NotifyEvidenceAnswer();
        }
    }
    public string AnswerStatus => answerStatus;
    public string AnswerText => answerText;
    public string AnswerCitationText => answerCitationText;
    public string AnswerReviewText => answerReview is { } review
        ? EvidenceAnswerResponse.Policy + "\n\nORIGINAL QUESTION:\n" + review.Question
            + "\n\nCOMPLETE IMMUTABLE PAGE (including stable record citations):\n" + review.Page
            + "\n\nConfirm ONE tool-free LocalOnly request in a NEW Local context conversation. "
            + "The question and validated final answer are retained under conversation retention. "
            + "Diagnostic sources keep their independent expiry; no source lifetime is renewed. "
            + Kora.Core.Context.LocalSourceRiskPolicy.Disclosure + " "
            + "No remote fallback, extra reads, tools, actions, replay or voice disclosure."
        : string.Empty;
    public IReadOnlyList<string> AnswerCitations => answerReview?.Records.Keys.Order(StringComparer.Ordinal).ToArray() ?? [];

    public void ReviewEvidenceAnswer()
    {
        if (!EnsureFindAdmission()) { return; }
        RetireEvidenceAnswer();
        if (!IsEvidenceAnswerInput(null))
        {
            answerStatus = "Denied: only original local user input can review this page.";
            NotifyEvidenceAnswer();
            return;
        }
        if (!CanReviewEvidenceAnswer)
        {
            answerStatus = "Unavailable. Explicitly Search a complete SQLite Log page first.";
            NotifyEvidenceAnswer();
            return;
        }
        if (page!.Records.Count == 0)
        {
            answerStatus = "NoEvidence: this exact page is empty. No model or conversation was requested.";
            NotifyEvidenceAnswer();
            return;
        }
        var text = resultText;
        var filters = filterRevision;
        var control = answerControlRevision!();
        var host = captureAnswerAdmission!();
        try
        {
            var review = new EvidenceAnswerReview(text, answerQuestion,
                HostActivity.Current?.Request ?? HostRequest.Create(RequestOrigin.LocalUi),
                answerTime.GetUtcNow());
            bool Current() => !closed && canInspect() && host() && control == answerControlRevision()
                && filterRevision == filters && ReferenceEquals(text, resultText)
                && answerTime.GetUtcNow() < review.ExpiresAt;
            if (!Current()) { throw new InvalidOperationException("The selected source admission changed."); }
            answerAdmitted = Current;
            answerReview = review;
            answerStatus = "Review needed: inspect the COMPLETE page and question below, then separately confirm once. "
                + "The shared source-use risk policy and a qualified local runtime must independently admit use; unavailable stays closed.";
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or JsonException or EncoderFallbackException)
        {
            Failure(logger, exception.GetType().Name);
            answerStatus = "Unavailable: invalid question or incomplete, expired, partial, excessive or unsupported page. "
                + "Question limit: 256 UTF-16 characters / 512 UTF-8 bytes. Nothing was truncated or sent.";
        }
        NotifyEvidenceAnswer();
    }

    public async Task ConfirmEvidenceAnswerAsync()
    {
        if (answering || answerConfirmed || answerReview is not { } review || answerAdmitted is not { } admitted
            || evidenceAnswers is null) { return; }
        if (!admitted() || !IsEvidenceAnswerInput(review))
        {
            RetireEvidenceAnswer();
            answerStatus = "Denied: this original local-user review is no longer current.";
            NotifyEvidenceAnswer();
            return;
        }
        var revision = answerRevision;
        using var cancellation = new CancellationTokenSource();
        answerCancellation = cancellation;
        answering = true;
        answerConfirmed = true;
        answerStatus = "One local request in progress. Cancel closes disclosure; resource ownership remains until actual join.";
        NotifyEvidenceAnswer();
        bool Current() => revision == answerRevision && admitted();
        using var activity = HostActivity.BeginRoot(review.Original, HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            var result = await evidenceAnswers.AnswerAsync(review, Current, cancellation.Token);
            if (!Current() || cancellation.IsCancellationRequested) { activity.Complete(HostOperationOutcome.Cancelled); return; }
            answerText = result.Outcome is EvidenceAnswerOutcome.Succeeded or EvidenceAnswerOutcome.Unsupported
                ? result.Receipt!.Message.AssistantResponse!.Body : string.Empty;
            answerStatus = $"Outcome: {result.Outcome}. "
                + (result.Receipt is not null ? "Actual durable conversation/task receipt confirmed. " : "No durable answer claimed. ")
                + "This one-shot review is consumed; no automatic retry.";
            activity.Complete(result.Outcome is EvidenceAnswerOutcome.Succeeded or EvidenceAnswerOutcome.Unsupported
                ? HostOperationOutcome.Completed : result.Outcome == EvidenceAnswerOutcome.Cancelled
                    ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            RetireEvidenceAnswer();
            answerStatus = "Unavailable or unconfirmed: inspect Sessions without retry. No answer or rollback is claimed.";
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            answerCancellation = null;
            answering = false;
            NotifyEvidenceAnswer();
        }
    }

    public void ShowEvidenceAnswerCitation(string citation)
    {
        if (!EnsureFindAdmission()) { return; }
        if (answerReview is not { } review || answerAdmitted?.Invoke() != true
            || !review.Records.TryGetValue(citation, out var text))
        {
            RetireEvidenceAnswer();
            answerStatus = "Unavailable: this citation is not admitted in the current immutable page. No source was read.";
            Failure(logger, nameof(InvalidDataException));
            NotifyEvidenceAnswer();
            return;
        }
        answerCitationText = text;
        NotifyEvidenceAnswer();
    }

    public void RefreshEvidenceAnswerAdmission()
    {
        if (!EnsureFindAdmission()) { return; }
        if (answerAdmitted is not null && !answerAdmitted()) { RetireEvidenceAnswer(); }
    }

    public void CancelEvidenceAnswer() => RetireEvidenceAnswer();

    private static bool IsEvidenceAnswerInput(EvidenceAnswerReview? review)
    {
        var input = HostActivity.Current;
        return !HostActivity.HasScope || input is { Outcome: HostOperationOutcome.Unknown }
            && !input.Activity!.IsStopped && input.Request.Origin == RequestOrigin.LocalUi && input.Request.InvocationId is null
            && (review is null || ReferenceEquals(review.Original, input.Request));
    }

    private void RetireEvidenceAnswer()
    {
        answerRevision++;
        answerCancellation?.Cancel();
        answerReview = null;
        answerAdmitted = null;
        answerConfirmed = false;
        if (closed || !canInspect()) { answerQuestion = string.Empty; }
        answerStatus = string.Empty;
        answerText = string.Empty;
        answerCitationText = string.Empty;
        NotifyEvidenceAnswer();
    }

    private void NotifyEvidenceAnswer()
    {
        OnPropertyChanged(nameof(HasEvidenceAnswers));
        OnPropertyChanged(nameof(CanReviewEvidenceAnswer));
        OnPropertyChanged(nameof(CanConfirmEvidenceAnswer));
        OnPropertyChanged(nameof(IsAnswering));
        OnPropertyChanged(nameof(AnswerQuestion));
        OnPropertyChanged(nameof(AnswerStatus));
        OnPropertyChanged(nameof(AnswerText));
        OnPropertyChanged(nameof(AnswerReviewText));
        OnPropertyChanged(nameof(AnswerCitations));
        OnPropertyChanged(nameof(AnswerCitationText));
    }
}
