using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Authorization;

namespace Kora;

internal sealed partial class QuestionWindowController
{
    internal async Task<ConversationQuestionPresentation> AskConversationAsync(Window owner, HostRequest request,
        HostRevision generation, QuestionSpec spec, DateTimeOffset expiresAt, Func<bool> admitted, CancellationToken token,
        HostOperationProposal? webProposal = null, Uri? webAddress = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
                AskConversationAsync(owner, request, generation, spec, expiresAt, admitted, token, webProposal, webAddress));
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
            }, token, webProposal, webAddress);
        }
        finally
        {
            window?.Close();
            state = null;
            running = false;
        }
    }
}
