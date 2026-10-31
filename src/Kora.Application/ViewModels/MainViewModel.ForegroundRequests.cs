using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ForegroundConversationTarget? foregroundConversationTarget;
    private SessionRequestReview? foregroundRequestReview;
    public event Action<ConversationSubmissionReceipt>? ForegroundRequestSubmitted;
    public bool HasForegroundRequestReview => foregroundRequestReview is not null;
    public string ForegroundRequestReviewText => foregroundRequestReview is { } review
        ? "Conversation: " + (review.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nProvider: Default / local only\nOriginal channel: " + review.Origin
            + "\nExpires: " + review.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            + (review.TaskResponseMode is { } mode ? "\nThis task only - declared output: " + mode
                + "\nConfigured output: " + review.ConfiguredTaskResponseMode
                + ". Effective output is revalidated at use; mandatory visual/privacy/call gates win. Saved preferences are unchanged." : string.Empty)
            + "\nNo earlier messages, memory or instructions are authorized by this request."
            + "\n\nComplete original message:\n" + review.Body
        : string.Empty;

    public void PublishForegroundConversationTarget(ForegroundConversationTarget? target)
    {
        var previous = Interlocked.Exchange(ref foregroundConversationTarget, target);
        if (ReferenceEquals(previous, target)) { return; }
        previous?.Retire();
        reviewedVoiceCommand?.Revalidate();
        completionNotices?.Revalidate();
        if (foregroundRequestReview is not null)
        {
            RetireSessionCommandReviews();
        }
    }

    public Task ConfirmForegroundRequestAsync() => CaptureForegroundRequestDecision(confirm: true)();

    public Task CancelForegroundRequestAsync() => CaptureForegroundRequestDecision(confirm: false)();

    public Func<Task> CaptureForegroundRequestDecision(bool confirm)
    {
        var review = foregroundRequestReview;
        var command = new SessionCommand(confirm ? SessionCommandOperation.RequestConfirm : SessionCommandOperation.RequestCancel,
            review?.Target.Authority.SessionId.Value) { RequestReviewId = review?.Id };
        return () => ExecuteSessionCommandAsync(command, SecurityAuditInitiator.LocalUser);
    }

    private void SetForegroundRequestReview(SessionRequestReview? review)
    {
        foregroundRequestReview = review;
        OnPropertyChanged(nameof(HasForegroundRequestReview));
        OnPropertyChanged(nameof(HasSessionOperationReview));
        OnPropertyChanged(nameof(ForegroundRequestReviewText));
    }
}
