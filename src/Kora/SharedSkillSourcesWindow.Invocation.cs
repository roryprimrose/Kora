using Avalonia.Controls;
using Kora.Core.Skills;

namespace Kora;

public sealed partial class SharedSkillSourcesWindow
{
    internal void BindInvocation(Func<Task> review) =>
        this.FindControl<Button>("ReviewInstructionUse")!.Click += async (_, _) =>
        { if (!closed && !busy && current()) { await review(); } };

    internal (SharedSkillSource Source, SharedSkillSnapshot Snapshot)? ResolveInvocation(string selection, SharedSkillSource source)
    {
        if (closed || busy || !current() || catalogue?.Source != source) { return null; }
        var name = selection;
        var marker = selection.IndexOf(" from source ", StringComparison.Ordinal);
        if (marker >= 0)
        {
            if (!Guid.TryParseExact(selection[(marker + " from source ".Length)..], "D", out var id) || id != source.Id)
            { return null; }
            name = selection[..marker];
        }
        var matches = catalogue.Packages.Where(item => string.Equals(item.Name, name, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? (source, matches[0]) : null;
    }
}
