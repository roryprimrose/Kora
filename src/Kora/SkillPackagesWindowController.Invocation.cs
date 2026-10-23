using Kora.Core.Skills;

namespace Kora;

internal sealed partial class SkillPackagesWindowController
{
    internal void BindInvocation(Func<SharedSkillSource, SharedSkillSnapshot, Task> route) => sharedSources.BindInvocation(route);
    internal (SharedSkillSource Source, SharedSkillSnapshot Snapshot)? ResolveInvocation(string selection) =>
        sharedSources.ResolveInvocation(selection);
}
