using Kora.Core.Skills;

namespace Kora;

internal sealed partial class SharedSkillSourcesWindowController
{
    private Func<SharedSkillSource, SharedSkillSnapshot, Task>? invocation;

    internal void BindInvocation(Func<SharedSkillSource, SharedSkillSnapshot, Task> route) => invocation = route;

    internal (SharedSkillSource Source, SharedSkillSnapshot Snapshot)? ResolveInvocation(string selection)
    {
        if (!Eligible() || window is not { } opened || opened.SelectedSource is not { } source) { return null; }
        return opened.ResolveInvocation(selection, source);
    }

    private async Task ReviewInvocationAsync(CancellationToken token)
    {
        if (window is not { } opened || !CurrentWindow(opened, token)
            || opened.SelectedSource is not { } source || opened.SelectedPackage is not { } snapshot
            || invocation is null)
        { throw new InvalidOperationException("Select an exact discovered source and skill before reviewing use."); }
        await invocation(source, snapshot);
    }
}
