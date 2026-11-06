using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal ConversationQuestionPresenter? QuestionPresenter { get; set; }
    private bool submittingClarification;
    public bool CanSubmitConversationWithClarification => CanSubmitConversation && QuestionPresenter is not null
        && !submittingClarification;
    public Task SubmitConversationWithClarificationAsync() => SubmitIndependentClarificationAsync(ModelToolMode.None);
    public Task SubmitReadOnlyWithClarificationAsync() =>
        SubmitIndependentClarificationAsync(ModelToolMode.ReadOnly);

    private async Task SubmitIndependentClarificationAsync(ModelToolMode mode)
    {
        if (!CanReadConversation || submittingClarification) { return; }
        if (QuestionPresenter is not { } presenter)
        {
            Failure(logger, nameof(InvalidOperationException));
            conversationStatus = "The exact native clarification presenter is unavailable. Nothing was submitted.";
            NotifyConversation();
            return;
        }
        var target = RequireSelected();
        var body = composerBody;
        var buffer = composerBufferRevision;
        var control = access.ControlRevision;
        var epoch = selectionEpoch;
        var token = lifetime.Token;
        var accepted = false;
        var released = false;
        bool SourceLive() => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control
            && (Volatile.Read(ref accepted) || selected == target && selectionEpoch == epoch);
        bool PresentLive() => SourceLive() && selected == target && selectionEpoch == epoch;
        submittingClarification = true;
        conversationBusy = true;
        busy = true;
        Notify();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            if (assistantReply)
            { throw new InvalidOperationException("Cancel exact-answer Reply review before submitting a separate clarification request."); }
            async Task<ConversationQuestionPresentation> Present(HostRequest request, HostRevision generation,
                Kora.Core.Interaction.QuestionSpec spec, DateTimeOffset expiresAt, Func<bool> admitted,
                CancellationToken cancellation)
            {
                return await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (!PresentLive() || !admitted() || cancellation.IsCancellationRequested
                        || request.SessionId != target.Authority.SessionId || generation != target.Authority.Generation
                        || request.Origin != RequestOrigin.LocalUi)
                    { throw new OperationCanceledException("Original clarification submission admission changed.", cancellation); }
                    // The canonical continuation calls its presenter only after this original task was retained and dispatched.
                    Volatile.Write(ref accepted, true);
                    if (composerBufferRevision == buffer && string.Equals(composerBody, body, StringComparison.Ordinal))
                    { composerBody = string.Empty; }
                    RememberUnsent();
                    conversationStatus = "Original request retained and dispatched. Answer its exact question independently of the selected conversation.";
                    conversationBusy = false;
                    busy = false;
                    released = true;
                    Notify();
                    return await presenter(request, generation, spec, expiresAt, admitted, cancellation);
                });
            }
            var receipt = mode switch
            {
                ModelToolMode.ReadOnly => await conversation!.SubmitReadOnlyWithClarificationAsync(target, body, SourceLive, Present, token),
                ModelToolMode.HostedClarification => await conversation!.SubmitClaudeWithClarificationAsync(target, body, SourceLive, Present, token),
                _ => await conversation!.SubmitAsync(target, body, SourceLive, token, questionPresenter: Present)
            };
            if (PresentLive())
            {
                if (!released && composerBufferRevision == buffer && string.Equals(composerBody, body, StringComparison.Ordinal))
                { composerBody = string.Empty; }
                RememberUnsent();
                if (PublishConversationCompletion(receipt.Message)) { conversationStatus = TerminalConversationStatus(receipt); }
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (exception is ConversationSubmissionUnconfirmedException && !closed && access.CanInspect
                && access.ControlRevision == control)
            {
                if (selected == target) { conversationHeld = true; }
                if (unsent.TryGetValue(target.Authority.SessionId, out var original)
                    && original.Authority == target.Authority && original.Control == control && !original.Held)
                {
                    unsent[target.Authority.SessionId] = original with { Held = true };
                    unsentRevision++;
                }
            }
            if (PresentLive())
            {
                conversationStatus = "Clarification submission unconfirmed: " + exception.Message
                    + " Inspect the original conversation/task receipts; no answer, rollback or retry is inferred.";
            }
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            if (!released) { conversationBusy = false; busy = false; }
            submittingClarification = false;
            Notify();
        }
    }
}
