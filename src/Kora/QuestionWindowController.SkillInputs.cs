using Avalonia.Controls;
using Avalonia.Threading;
using Kora.Application.Skills;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class QuestionWindowController
{
    internal async Task<HostInteractionDecision> AskSkillInputAsync(Window owner, OwnedSkillInputQuestions collection,
        QuestionSpec spec, Func<bool> admitted, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => AskSkillInputAsync(owner, collection, spec, admitted, token));
        }
        token.ThrowIfCancellationRequested();
        if (disposed || IsOwnerInUse || main.ComposerDictation?.IsActive == true
            || !main.CanRevealPrivatePresentation || !admitted() || !owner.IsVisible)
        { throw new InvalidOperationException("The exact native question presenter is unavailable or occupied."); }
        main.RetireCurrentResponseSpeech();
        running = true;
        try
        {
            return await host.AskSkillInputAsync(collection, spec, admitted, model =>
            {
                token.ThrowIfCancellationRequested();
                if (!admitted() || disposed || !owner.IsVisible)
                { throw new OperationCanceledException("The original skill input presentation changed.", token); }
                OpenExact(owner, model);
                if (window is null || !ReferenceEquals(state, model))
                { throw new InvalidOperationException("The exact literal input window could not be opened."); }
                return Task.CompletedTask;
            }, token);
        }
        finally { window?.Close(); state = null; running = false; }
    }
}
