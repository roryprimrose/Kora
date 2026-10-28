using System.Globalization;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionRequestCommandService? failedRetryCommands;
    private SessionRequestReview? failedRetryReview;
    public bool CanReviewFailedRetry => CanReadConversation && access.CanControl && selected?.Authority.IsActive == true
        && selectedAnswerRecord?.Outcome is not null;
    public bool CanConfirmFailedRetry => CanReadConversation && access.CanControl && failedRetryReview is not null;
    public string FailedRetryReviewText => !closed && access.CanInspect && failedRetryReview is { FailedSource: { } source } review
        ? "NEW attempt, not replay. Original failure and unsent input remain unchanged.\n"
            + "Conversation: " + (review.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nOriginal channel: " + review.Origin + " | provider: " + source.Provider.Provider
            + " | model revision: " + source.Provider.Revision.Value.ToString(CultureInfo.InvariantCulture)
            + "\nOriginal message sequence: " + source.Message.Sequence.ToString(CultureInfo.InvariantCulture)
            + " | message revision: " + source.Message.Revision.ToString(CultureInfo.InvariantCulture)
            + " | terminal task revision: " + source.Task.Revision.Value.ToString(CultureInfo.InvariantCulture)
            + "\nCurrent account/profile: " + (review.RetryProviderProfile?.ToString() ?? "Local host policy; no hosted disclosure")
            + "\nReview expires UTC: " + review.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            + "\n" + review.Body
            + "\nConfirm separately once. Hosted sends still require current native final-byte approval. Existing held queues require their separate explicit Resume; no queue authority is renewed."
        : string.Empty;

    public Task ReviewFailedRetryAsync() => RunConversationAsync(async (target, live) =>
    {
        ClearFailedRetry();
        var source = selectedAnswerRecord
            ?? throw new InvalidOperationException("Select one exact failed USER request from the observed conversation.");
        if (!ConversationMessages.Any(item => ReferenceEquals(item, source)))
        { throw new InvalidOperationException("The selected failed message is no longer observed. Refresh explicitly."); }
        var selection = Interlocked.Read(ref answerSelectionRevision);
        bool SourceLive() => live() && access.CanControl && selection == Interlocked.Read(ref answerSelectionRevision)
            && ReferenceEquals(selectedAnswerRecord, source);
        failedRetryCommands ??= conversation!.CreateRequestCommands();
        var result = await failedRetryCommands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
            target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
            { RequestFailedMessage = source }, RequestOrigin.LocalUi, SourceLive, lifetime.Token);
        if (!SourceLive()) { throw new OperationCanceledException("The original failed selection changed."); }
        failedRetryReview = result.RequestReview;
        conversationStatus = "Review the complete immutable failed request and original provider. No new task, send, composer change or saved draft.";
    });

    public Task ConfirmFailedRetryAsync() => RunConversationAsync(async (_, _) =>
    {
        var review = failedRetryReview ?? throw new InvalidOperationException("Review one exact failed request before confirmation.");
        failedRetryReview = null;
        var result = await failedRetryCommands!.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
            review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id },
            RequestOrigin.LocalUi, () => !closed && access.CanInspect && access.CanControl, lifetime.Token);
        var receipt = result.RequestSubmission!;
        if (receipt.Message.Outcome is not null)
        {
            conversationPage = null;
            completedConversationMessage = receipt.Message;
        }
        conversationStatus = "New retry attempt: HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
            + " | task " + receipt.Task.State
            + (receipt.Message.Outcome is null ? ". Accepted in the existing queue; inspect pending work."
                : receipt.Message.AssistantResponse is null ? ". No answer retained." : ". Genuine ASSISTANT answer retained once.")
            + " Original failure and unsent input unchanged. Refresh conversation for both records.";
    }, submission: true);

    public void CancelFailedRetry()
    {
        ClearFailedRetry();
        conversationStatus = "Failed-request review declined. No new attempt or composer change.";
        NotifyConversation();
    }

    private void ClearFailedRetry()
    {
        failedRetryCommands?.Retire();
        failedRetryReview = null;
    }

    private void NotifyFailedRetry()
    {
        OnPropertyChanged(nameof(CanReviewFailedRetry));
        OnPropertyChanged(nameof(CanConfirmFailedRetry));
        OnPropertyChanged(nameof(FailedRetryReviewText));
    }
}
