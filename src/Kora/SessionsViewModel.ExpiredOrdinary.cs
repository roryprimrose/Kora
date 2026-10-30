using System.Globalization;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionRequestCommandService? expiredOrdinaryCommands;
    private SessionRequestReview? expiredOrdinaryReview;
    private Func<bool>? expiredOrdinaryCurrent;
    internal Func<Func<bool>>? CaptureFreshOrdinaryInput { get; set; }

    public bool CanReviewExpiredOrdinary => CanReadConversation && access.CanControl && selected?.Authority.IsActive == true
        && CaptureFreshOrdinaryInput is not null && selectedAnswerRecord is
            { OrdinaryText: true, Version: 1, Revision: 2, Outcome: ModelTurnOutcome.Denied, Reason: ModelTurnReason.ContextExpired };
    public bool CanConfirmExpiredOrdinary => CanCompose && expiredOrdinaryReview is not null
        && expiredOrdinaryCurrent?.Invoke() == true;
    public string ExpiredOrdinaryReviewText => !closed && access.CanInspect && expiredOrdinaryReview is { ExpiredSource: { } source } review
        && expiredOrdinaryCurrent?.Invoke() == true
        ? "NEW ordinary LOCAL request, not retry, replay or renewed authority.\nConversation: "
            + (review.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nOriginal USER source channel: " + source.Message.Request.Origin
            + " | NEW initiator: LocalUi"
            + "\nHost-confirmed before dispatch: Denied / ContextExpired; no execution record."
            + "\nOriginal path: local tool-free bare text; original model/account/profile not recorded, unknown (not inferred)."
            + "\nCURRENT provider: " + review.CurrentLocalProvider
            + "\nCURRENT profile: " + (review.RetryProviderProfile?.ToString() ?? "Unconfigured local host policy; no account/profile tuple")
            + "\nReview expires UTC: " + review.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            + "\nConfirm separately once to retain the complete unchanged body as a NEW USER request/task at the normal FIFO tail."
            + " Its 15-second context begins at this NEW confirmation, not the old submission or review."
            + " No old context, instructions, memory, evidence, consent, grants, tools or hosted transfer is reused."
            + " This subset refuses an applying session instruction; use its independent current source-review route."
            + " Held dispatch still needs independent Resume, which invalidates this review: Resume first, then review again."
            + " Normal capacity, message, provider, input and resource limits apply. Unqualified runtime stays Unavailable."
            + "\nOriginal history/outcome and unrelated unsent input remain unchanged.\n\nComplete immutable original USER body:\n" + review.Body
        : string.Empty;

    public Task ReviewExpiredOrdinaryAsync() => RunConversationAsync(async (target, live) =>
    {
        ClearExpiredOrdinary();
        var source = selectedAnswerRecord ?? throw new InvalidOperationException("Select one expired ordinary USER record.");
        if (!ConversationMessages.Any(item => ReferenceEquals(item, source)))
        { throw new InvalidOperationException("Select an actual currently observed original USER row; reconstructed sources are unavailable."); }
        var input = (CaptureFreshOrdinaryInput
            ?? throw new InvalidOperationException("Fresh original native input admission is unavailable."))();
        var selection = Interlocked.Read(ref answerSelectionRevision);
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var dictation = dictationSourceRevision;
        bool SourceLive() => live() && input() && access.CanControl
            && selection == Interlocked.Read(ref answerSelectionRevision) && ReferenceEquals(selectedAnswerRecord, source)
            && buffer == Interlocked.Read(ref composerBufferRevision) && dictation == dictationSourceRevision
            && Dictation?.IsActive != true;
        expiredOrdinaryCommands ??= conversation!.CreateRequestCommands();
        var result = await expiredOrdinaryCommands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
            target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
            { RequestExpiredMessage = source }, RequestOrigin.LocalUi, SourceLive, lifetime.Token);
        if (!SourceLive()) { throw new OperationCanceledException("The selected source, input, capture or private admission changed."); }
        expiredOrdinaryReview = result.RequestReview;
        expiredOrdinaryCurrent = SourceLive;
        conversationStatus = "Review the complete expired source and current local request consequences. Nothing accepted, renewed or dispatched.";
    });

    internal Func<Task> CaptureExpiredOrdinaryDecision(bool confirm)
    {
        var expected = expiredOrdinaryReview;
        return () => confirm ? ConfirmExpiredOrdinaryAsync(expected) : CancelExpiredOrdinaryAsync(expected);
    }

    public Task ConfirmExpiredOrdinaryAsync() => ConfirmExpiredOrdinaryAsync(expiredOrdinaryReview);

    private Task ConfirmExpiredOrdinaryAsync(SessionRequestReview? expected) => RunConversationAsync(async (_, _) =>
    {
        var review = expiredOrdinaryReview ?? throw new InvalidOperationException("Review one exact expired ordinary USER source first.");
        if (!ReferenceEquals(review, expected))
        { throw new InvalidOperationException("The fresh review changed. An older click cannot confirm newer content."); }
        expiredOrdinaryReview = null;
        var result = await expiredOrdinaryCommands!.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
            review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id },
            RequestOrigin.LocalUi, () => !closed && access.CanInspect && access.CanControl, lifetime.Token);
        conversationPage = null;
        completedConversationMessage = result.RequestSubmission!.Message;
        conversationStatus = "NEW ordinary request accepted separately: HOST " + result.RequestSubmission.Message.Outcome
            + " / " + result.RequestSubmission.Message.Reason + " | task " + result.RequestSubmission.Task.State
            + ". Original expired history and unsent input unchanged. A pending request has not executed; inspect submitted work.";
    }, submission: true);

    private Task CancelExpiredOrdinaryAsync(SessionRequestReview? expected)
    {
        if (ReferenceEquals(expected, expiredOrdinaryReview)) { CancelExpiredOrdinary(); }
        return Task.CompletedTask;
    }

    public void CancelExpiredOrdinary()
    {
        ClearExpiredOrdinary();
        conversationStatus = "Fresh expired-text review declined. No request accepted, authority renewed, activity extended or composer changed.";
        NotifyConversation();
    }

    private void ClearExpiredOrdinary()
    {
        expiredOrdinaryCommands?.Retire();
        expiredOrdinaryReview = null;
        expiredOrdinaryCurrent = null;
    }

    private void NotifyExpiredOrdinary()
    {
        OnPropertyChanged(nameof(CanReviewExpiredOrdinary));
        OnPropertyChanged(nameof(CanConfirmExpiredOrdinary));
        OnPropertyChanged(nameof(ExpiredOrdinaryReviewText));
    }
}
