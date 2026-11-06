using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionRequestCommandService? composerRequests;
    private SessionRequestReview? composerRequestReview;
    private Func<bool>? composerRequestCurrent;
    private Action? composerRequestAccepted;
    private long composerRequestEpoch;
    private bool preparingComposerRequest;

    public bool CanReviewComposerRequest => CanSubmitConversation && !assistantReply && !preparingComposerRequest;
    public bool CanConfirmComposerRequest => CanCompose && composerRequestReview is not null
        && composerRequestCurrent?.Invoke() == true;
    public bool CanCancelComposerRequest => !closed && access.CanInspect && access.CanControl
        && (preparingComposerRequest || composerRequestReview is not null);
    public string ComposerRequestReviewText => !closed && access.CanInspect && composerRequestCurrent?.Invoke() == true
        && composerRequestReview is { } review
        ? "Review unsent message once - nothing submitted or saved.\nConversation: "
            + (review.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nDefault local-only request; no earlier messages or memory added."
            + "\nAny separately applied session instruction scope still applies."
            + "\nOriginal channel: " + review.Origin + "\nExpires UTC: " + review.ExpiresAt.ToString("O")
            + "\n\nComplete unchanged message:\n" + review.Body
            + "\n\nConfirm separately once, or Cancel and keep the unsent input. Direct Send remains separate."
        : string.Empty;

    internal Func<Task> CaptureComposerRequestReview()
    {
        var target = selected;
        var buffer = composerBufferRevision;
        var selection = selectionEpoch;
        var control = access.ControlRevision;
        var body = composerBody;
        return () => ReviewComposerRequestAsync(target, body, buffer, selection, control);
    }

    public Task ReviewComposerRequestAsync() => CaptureComposerRequestReview()();

    private Task ReviewComposerRequestAsync(SessionWorkspaceEntry? expected, string body, long buffer, long selection, long control) =>
        RunConversationAsync(async (target, live) =>
        {
            if (target != expected || composerBufferRevision != buffer || selectionEpoch != selection
                || access.ControlRevision != control || !access.CanControl || !target.Authority.IsActive
                || conversationHeld || conversationSourceHeld || string.IsNullOrWhiteSpace(body) || assistantReply
                || !string.Equals(body, composerBody, StringComparison.Ordinal))
            { throw new InvalidOperationException("The original unsent message or conversation changed. Start a fresh review; nothing submitted."); }
            RetireComposerRequest();
            var attempt = composerRequestEpoch;
            var accepted = 0;
            bool Current() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control
                && (Volatile.Read(ref accepted) != 0 || live() && composerRequestEpoch == attempt
                    && composerBufferRevision == buffer && string.Equals(body, composerBody, StringComparison.Ordinal));
            composerRequestCurrent = Current;
            composerRequestAccepted = () => Interlocked.Exchange(ref accepted, 1);
            preparingComposerRequest = true;
            NotifyComposerRequest();
            try
            {
                composerRequests ??= conversation!.CreateRequestCommands();
                var result = await composerRequests.ExecuteAsync(new(SessionCommandOperation.RequestReview,
                    target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
                { RequestBody = body }, RequestOrigin.LocalUi, Current, lifetime.Token);
                if (!Current()) { throw new OperationCanceledException("The original composer review was retired. Nothing submitted."); }
                composerRequestReview = result.RequestReview
                    ?? throw new InvalidDataException("The exact composer request review was not returned.");
                conversationStatus = "Review the complete unchanged unsent message and original conversation. Confirm separately once, or Cancel without submission.";
            }
            finally { preparingComposerRequest = false; NotifyComposerRequest(); }
        });

    internal Func<Task> CaptureComposerRequestDecision(bool confirm)
    {
        var review = composerRequestReview;
        var attempt = composerRequestEpoch;
        return () => confirm ? ConfirmComposerRequestAsync(review, attempt) : CancelComposerRequest(review, attempt);
    }

    private Task ConfirmComposerRequestAsync(SessionRequestReview? expected, long attempt) =>
        RunConversationAsync(async (target, live) =>
        {
            var review = composerRequestReview;
            if (review is null || !ReferenceEquals(review, expected) || composerRequestEpoch != attempt
                || target != review.Target || composerRequestCurrent?.Invoke() != true)
            { throw new InvalidOperationException("The exact composer request review changed or was cancelled. No newer review was confirmed."); }
            var buffer = composerBufferRevision;
            var source = composerRequestCurrent
                ?? throw new InvalidOperationException("The original composer source owner is unavailable.");
            var accepted = composerRequestAccepted
                ?? throw new InvalidOperationException("The original composer acceptance owner is unavailable.");
            composerRequestReview = null;
            var result = await composerRequests!.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
                review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id },
                RequestOrigin.LocalUi, source, lifetime.Token);
            var receipt = result.RequestSubmission
                ?? throw new InvalidDataException("The original composer submission receipt was not returned.");
            accepted();
            composerRequestCurrent = null;
            composerRequestAccepted = null;
            if (!live()) { throw new OperationCanceledException("The original composer submission presentation changed. Inspect its exact receipts."); }
            if (composerBufferRevision == buffer && string.Equals(composerBody, review.Body, StringComparison.Ordinal))
            {
                composerBody = string.Empty;
                Interlocked.Increment(ref composerBufferRevision);
            }
            RememberUnsent();
            PublishConversationCompletion(receipt.Message);
            conversationStatus = receipt.Message.Outcome is null ? result.Message : TerminalConversationStatus(receipt);
        }, submission: true);

    private Task CancelComposerRequest(SessionRequestReview? expected, long attempt)
    {
        if (composerRequestEpoch != attempt || !ReferenceEquals(composerRequestReview, expected))
        {
            Failure(logger, nameof(InvalidOperationException));
            conversationStatus = "The earlier composer review changed. No newer review or unsent input was cancelled.";
        }
        else
        {
            RetireComposerRequest();
            conversationStatus = "Composer request review cancelled. Unsent input retained for its original conversation; nothing submitted or saved.";
        }
        NotifyConversation();
        return Task.CompletedTask;
    }

    private void RetireComposerRequest()
    {
        composerRequestEpoch++;
        composerRequests?.Retire();
        composerRequestReview = null;
        composerRequestCurrent = null;
        composerRequestAccepted = null;
        preparingComposerRequest = false;
    }

    private void NotifyComposerRequest()
    {
        if (composerRequestCurrent?.Invoke() == false) { RetireComposerRequest(); }
        OnPropertyChanged(nameof(CanReviewComposerRequest));
        OnPropertyChanged(nameof(CanConfirmComposerRequest));
        OnPropertyChanged(nameof(CanCancelComposerRequest));
        OnPropertyChanged(nameof(ComposerRequestReviewText));
    }
}
