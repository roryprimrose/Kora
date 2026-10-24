using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionAttachmentWindowController
{
    internal async Task OpenAnswer(SessionWorkspaceEntry target, SessionConversationService conversation,
        Func<SessionAttachmentAnswerReview, Func<bool>, CancellationToken, Task<ConversationSubmissionReceipt>> submit,
        Func<bool> admitted, CancellationToken token)
    {
        await Open(target, attach: false, admitted, token);
        var view = window ?? throw new InvalidOperationException("The exact retained source viewer is unavailable.");
        var capturedLifetime = lifetime ?? throw new InvalidOperationException("The source viewer lifetime was retired.");
        bool Current() => ReferenceEquals(window, view) && ReferenceEquals(lifetime, capturedLifetime)
            && eligible?.Invoke() == true && admitted();
        var source = await service.Read(target.Authority.SessionId, capturedLifetime.Token)
            ?? throw new InvalidOperationException("This session has no retained attachment.");
        if (!Current()) { throw new OperationCanceledException("The source selection changed."); }
        view.ShowAttachmentAnswer(source,
            (question, live, cancellation) => conversation.PrepareAttachmentAnswerAsync(target, source, question,
                () => Current() && live(), cancellation),
            (review, live, cancellation) => submit(review, () => Current() && live(), cancellation), Current, capturedLifetime.Token);
    }
}
