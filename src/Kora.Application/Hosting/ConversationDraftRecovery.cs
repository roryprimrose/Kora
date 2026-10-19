using Kora.Application.Presentation;
using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>One explicit read/review/readback; this workflow has no persistence or submission capability.</summary>
public static class ConversationDraftRecovery
{
    public static async Task<ConversationDraftSnapshot?> ReviewAsync(SessionWorkspaceEntry target,
        ConversationDraftSnapshot original, string raw,
        Func<CancellationToken, Task<ConversationDraftSnapshot>> readLatest,
        Func<ConversationDraftRecoveryReview, CancellationToken, Task<bool>> exactReview,
        Func<bool> current, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(readLatest);
        ArgumentNullException.ThrowIfNull(exactReview);
        ArgumentNullException.ThrowIfNull(current);
        ConversationDraftRecoveryReview.RequireObservation(target, original);
        ConversationText.Validate(raw);
        void RequireCurrent()
        {
            token.ThrowIfCancellationRequested();
            if (!current()) { throw new OperationCanceledException("Draft recovery admission or exact composer revision changed."); }
        }
        RequireCurrent();
        var latest = await readLatest(token);
        RequireCurrent();
        using var review = new ConversationDraftRecoveryReview(target, original, latest, raw, current);
        if (!await exactReview(review, token))
        {
            RequireCurrent();
            return null;
        }
        RequireCurrent();
        if (!review.Available) { throw new OperationCanceledException("The draft recovery review was retired."); }
        var confirmed = await readLatest(token);
        RequireCurrent();
        ConversationDraftRecoveryReview.RequireObservation(target, confirmed);
        if (confirmed != latest)
        { throw new InvalidOperationException("The exact latest saved draft changed during review. Review again; old baseline and raw buffer retained."); }
        if (!review.Available) { throw new OperationCanceledException("The draft recovery review expired before adoption."); }
        return confirmed;
    }
}
