using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Authorization;
using Kora.Application.Presentation;

namespace Kora;

internal sealed partial class QuestionWindowController
{
    internal IPendingQuestionFocus? CaptureConversationNoticeQuestion(LocalEvent notice)
    {
        var original = state;
        var native = window;
        if (disposed || original is null || native is null
            || notice.Source != LocalEventSource.ConversationQuestion
            || original.Key.Request.SessionId != notice.SessionId || original.Key.Request.TaskId != notice.TaskId
            || original.Key.QuestionId.Value != notice.SubjectId || original.Key.Revision.Value != notice.SourceRevision
            || original.Question.SessionGeneration.Value != notice.Generation || original.Question.ExpiresAt != notice.ExpiresAt)
        { return null; }
        return CaptureExactFocus(original, native,
            () => host.ValidateConversationNoticeAsync(original, notice, CancellationToken.None),
            main.CaptureQuestionNoticeNavigationEligibility());
    }

    internal async Task<ConversationQuestionPresentation> AskConversationAsync(Window owner, HostRequest request,
        HostRevision generation, QuestionSpec spec, DateTimeOffset expiresAt, Func<bool> admitted, CancellationToken token,
        HostOperationProposal? webProposal = null, Uri? webAddress = null,
        Kora.Application.Mcp.McpReview? mcpReview = null, bool mcpSelection = false, bool mcpInput = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
                AskConversationAsync(owner, request, generation, spec, expiresAt, admitted, token, webProposal, webAddress,
                    mcpReview, mcpSelection, mcpInput));
        }
        token.ThrowIfCancellationRequested();
        if (disposed || IsOwnerInUse || !main.CanRevealPrivatePresentation || !admitted() || !owner.IsVisible)
        { throw new InvalidOperationException("The exact native question owner is unavailable or occupied. No reply target was followed."); }
        main.RetireCurrentResponseSpeech();
        running = true;
        try
        {
            return await host.AskConversationAsync(request, generation, spec, expiresAt, admitted, model =>
            {
                token.ThrowIfCancellationRequested();
                if (!admitted() || disposed || !owner.IsVisible)
                { throw new OperationCanceledException("Original conversation presentation changed.", token); }
                OpenExact(owner, model);
                if (window is null || !ReferenceEquals(state, model))
                { throw new InvalidOperationException("The exact native question window could not be opened."); }
                return Task.CompletedTask;
            }, token, webProposal, webAddress, mcpReview, mcpSelection, mcpInput);
        }
        finally
        {
            window?.Close();
            state = null;
            running = false;
        }
    }
}
