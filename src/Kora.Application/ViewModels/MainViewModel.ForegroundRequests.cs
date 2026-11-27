using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ForegroundConversationTarget? foregroundConversationTarget;
    private ForegroundConversationTarget? voiceConversationTarget;
    private long foregroundConversationRevision;
    private SessionRequestReview? foregroundRequestReview;
    public event Action<ConversationSubmissionReceipt>? ForegroundRequestSubmitted;
    public bool HasForegroundRequestReview => foregroundRequestReview is not null;
    public string ForegroundRequestReviewText => foregroundRequestReview is { } review
        ? "Conversation: " + (review.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nProvider: " + (IsApproveAllReadback ? "Settings-selected provider" : "Default / local only")
            + "\nOriginal channel: " + review.Origin
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
        var resourceRetired = voiceConversationTarget is { IsResourceCurrent: false };
        if (voiceConversationTarget is { IsResourceCurrent: false } retiredTarget)
        {
            retiredTarget.Retire();
            voiceConversationTarget = null;
            Interlocked.Increment(ref foregroundConversationRevision);
        }
        if (ReferenceEquals(previous, target) && !resourceRetired) { return; }
        previous?.ReleaseForeground();
        if (target is not null && (previous is not null || voiceConversationTarget?.Entry != target.Entry))
        {
            voiceConversationTarget?.Retire();
            voiceConversationTarget = target;
            Interlocked.Increment(ref foregroundConversationRevision);
        }
        completionNotices?.Revalidate();
        if (foregroundRequestReview is { } review
            && (review.Origin != Kora.Core.Hosting.RequestOrigin.ActivatedVoice
                || voiceConversationTarget?.IsResourceCurrent != true))
        {
            RetireSessionCommandReviews();
        }
        sessionRoutingVoiceReply?.Revalidate();
    }

    public Task ConfirmForegroundRequestAsync() => CaptureForegroundRequestDecision(confirm: true)();

    public Task CancelForegroundRequestAsync() => CaptureForegroundRequestDecision(confirm: false)();

    public Func<Task> CaptureForegroundRequestDecision(bool confirm)
    {
        if (confirm && IsApproveAllReadback) { return static () => Task.CompletedTask; }
        var review = foregroundRequestReview;
        var command = new SessionCommand(confirm ? SessionCommandOperation.RequestConfirm : SessionCommandOperation.RequestCancel,
            review?.Target.Authority.SessionId.Value)
        { RequestReviewId = review?.Id };
        return () => sessionRequestNativeDecision = DecideAsync();

        async Task DecideAsync()
        {
            if (confirm && IsApproveAllReadback) { return; }
            if (sessionRoutingVoiceReply is { } reply)
            {
                await reply.DisposeAsync();
                ReleaseSessionRoutingVoiceReply(reply);
            }
            await ExecuteSessionCommandAsync(command, SecurityAuditInitiator.LocalUser);
        }
    }

    private void SetForegroundRequestReview(SessionRequestReview? review)
    {
        if (!ReferenceEquals(foregroundRequestReview, review)) { sessionRoutingVoiceReply?.Cancel(); }
        foregroundRequestReview = review;
        uiDispatcher.Post(() =>
        {
            OnPropertyChanged(nameof(HasForegroundRequestReview));
            OnPropertyChanged(nameof(CanConfirmForegroundRequest));
            OnPropertyChanged(nameof(HasSessionOperationReview));
            OnPropertyChanged(nameof(ForegroundRequestReviewText));
            OnPropertyChanged(nameof(OfferDetailsOnce));
        });
    }
}