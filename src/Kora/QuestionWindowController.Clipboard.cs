using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class QuestionWindowController
{
    internal async Task<HostInteractionDecision> RequestClipboardPermissionAsync(Window owner,
        HostOperationProposal proposal, HostRevision generation, Func<bool> admitted, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
                RequestClipboardPermissionAsync(owner, proposal, generation, admitted, token));
        }
        token.ThrowIfCancellationRequested();
        if (disposed || IsOwnerInUse || !main.CanRevealPrivatePresentation || !admitted() || !owner.IsVisible)
        { throw new InvalidOperationException("The original clipboard permission window is unavailable or occupied."); }
        main.RetireCurrentResponseSpeech();
        running = true;
        try
        {
            return await host.RequestClipboardPermissionAsync(proposal, generation, admitted, model =>
            {
                if (!admitted() || disposed || !owner.IsVisible)
                { throw new OperationCanceledException("Original clipboard presentation changed.", token); }
                OpenExact(owner, model);
                if (window is null || !ReferenceEquals(state, model))
                { throw new InvalidOperationException("The clipboard permission window could not be opened."); }
                return Task.CompletedTask;
            }, token);
        }
        finally
        {
            window?.Close();
            state = null;
            running = false;
        }
    }
}
