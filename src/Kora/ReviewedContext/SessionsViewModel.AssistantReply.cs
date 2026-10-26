using Kora.Application.Hosting;
using Kora.Application.Hosting.ReviewedContext;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private bool assistantReply;
    private ConversationMessage? replySource;
    private Func<bool> replySourceAdmission = static () => false;
    private MixedContextWorkflow? replyWorkflow;
    private ConversationHistoryReview? replyReview;
    private long replyRevision;

    public bool HasAssistantReply => assistantReply;
    public bool CanReplyToAnswer => CanCompose;
    private bool ReplySourceCurrent => assistantReply && replySource is not null && replySourceAdmission();
    public string ReplySourceText => ReplySourceCurrent
        ? "Replying to this exact earlier ASSISTANT answer (local-only inert evidence):\n" + replySource!.AssistantResponse!.Body
        : assistantReply ? "Reply source unavailable or changed. Cancel reply, or explicitly read and choose an answer again. Sending as plain input is blocked."
        : string.Empty;
    public string ReplyReviewText => ReplySourceCurrent ? replyReview?.Text ?? string.Empty : string.Empty;
    public bool CanReviewAssistantReply => CanCompose && ReplySourceCurrent && ComposerDirty;
    public bool CanConfirmAssistantReply => CanReviewAssistantReply && replyReview?.IsCurrent == true;

    public Task ReplyToAnswerAsync(ConversationMessage message, Func<bool>? presenterAdmission = null) => RunConversationAsync((target, live) =>
    {
        if (!CanReplySource(message) || presenterAdmission?.Invoke() == false)
        {
            assistantReply = true;
            InvalidateReplySource();
            RememberUnsent();
            throw new InvalidOperationException("Reply requires one actual retained ASSISTANT answer from the currently displayed conversation page.");
        }
        ClearAssistantReply();
        ClearHistoryContext();
        ClearMixedContext();
        var page = conversationPage;
        var completed = completedConversationMessage;
        var control = access.ControlRevision;
        var selection = selectionEpoch;
        assistantReply = true;
        replySource = message;
        replySourceAdmission = () => live() && access.CanControl && access.ControlRevision == control
            && presenterAdmission?.Invoke() != false
            && selectionEpoch == selection && selected == target && target.Authority.IsActive
            && ReferenceEquals(conversationPage, page) && ReferenceEquals(completedConversationMessage, completed)
            && ConversationMessages.Any(row => ReferenceEquals(row, message));
        replyWorkflow = conversation!.CreateReplyContextWorkflow();
        RememberUnsent();
        conversationStatus = "Reply selected. Keep your new follow-up in this session's memory-only composer. "
            + "Review the exact answer and unchanged follow-up, then separately confirm one local-only answer. Nothing submitted.";
        return Task.CompletedTask;
    }, replyOperation: true);

    private bool CanReplySource(ConversationMessage message) => access.CanControl && selected?.Authority.IsActive == true
        && message.AssistantResponse is not null && ConversationMessages.Any(row => ReferenceEquals(row, message));

    public Task ReviewAssistantReplyAsync() => RunConversationAsync(async (target, live) =>
    {
        RetireReplyReview();
        if (!ReplySourceCurrent)
        { throw new InvalidOperationException("The exact reply source is no longer presented. Cancel reply or explicitly choose it again."); }
        var source = replySource!;
        var version = replyRevision;
        var buffer = composerBufferRevision;
        var body = composerBody;
        bool Original() => live() && ReplySourceCurrent && replyRevision == version && composerBufferRevision == buffer
            && string.Equals(composerBody, body, StringComparison.Ordinal);
        try
        {
            replyReview = await (replyWorkflow ?? throw new InvalidOperationException("Choose an exact answer again."))
                .ReviewReplyAsync(target, body, source, Original, lifetime.Token);
            conversationStatus = "Inspect the complete earlier ASSISTANT evidence and unchanged NEW USER request. "
                + "Separately confirm ONE local-only tool-free answer. The earlier original USER is not included.";
        }
        catch
        {
            InvalidateReplySource();
            throw;
        }
    }, replyOperation: true);

    public Task ConfirmAssistantReplyAsync() => RunConversationAsync(async (_, live) =>
    {
        var review = replyReview ?? throw new InvalidOperationException("Review the exact reply source and unchanged follow-up first.");
        var body = composerBody;
        try
        {
            var receipt = await (replyWorkflow ?? throw new InvalidOperationException("The reply workflow was retired."))
                .ConfirmReplyAsync(review, () => live() && ReplySourceCurrent, lifetime.Token);
            if (!live() || !ReplySourceCurrent)
            { throw new OperationCanceledException("The original reply presentation was revoked."); }
            completedConversationMessage = receipt.Message;
            conversationPage = null;
            if (string.Equals(composerBody, body, StringComparison.Ordinal))
            { composerBody = string.Empty; Interlocked.Increment(ref composerBufferRevision); }
            ClearAssistantReply();
            RememberUnsent();
            conversationStatus = "Reply retained in the same conversation. HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
                + (receipt.Message.AssistantResponse is null ? ". No assistant answer was produced." : ". Genuine ASSISTANT answer retained separately.")
                + " No source-body copy, tools, hosted transfer, grants, memory write, fallback or automatic retry.";
        }
        catch
        {
            InvalidateReplySource();
            throw;
        }
        finally { RetireReplyReview(); }
    }, submission: true, replyOperation: true);

    public void CancelAssistantReply()
    {
        ClearAssistantReply();
        RememberUnsent();
        conversationStatus = "Reply cancelled. Unsent follow-up unchanged; nothing submitted or recalled. "
            + "Any already-dispatched provider remains owned until actual completion; no late reply is admitted.";
        NotifyConversation();
    }

    private void RetireReplyReview()
    {
        replyRevision++;
        replyWorkflow?.Retire();
        replyReview?.Dispose();
        replyReview = null;
    }

    private void InvalidateReplySource()
    {
        RetireReplyReview();
        replySource = null;
        replySourceAdmission = static () => false;
    }

    private void ClearAssistantReply()
    {
        InvalidateReplySource();
        replyWorkflow?.Dispose();
        replyWorkflow = null;
        assistantReply = false;
    }

    private void NotifyAssistantReply()
    {
        if (assistantReply && !ReplySourceCurrent) { InvalidateReplySource(); }
        if (replyReview is { IsCurrent: false }) { RetireReplyReview(); }
        foreach (var name in new[] { nameof(HasAssistantReply), nameof(CanReplyToAnswer), nameof(ReplySourceText),
            nameof(ReplyReviewText), nameof(CanReviewAssistantReply), nameof(CanConfirmAssistantReply) })
        { OnPropertyChanged(name); }
    }
}
