using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalWorkbenchPlacementPreferences : IWorkbenchPlacementPreferences
{
    private const string FileName = "workbench-placement.txt";
    private readonly IPreferenceStore store;

    public LocalWorkbenchPlacementPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalWorkbenchPlacementPreferences(IPreferenceStore store) => this.store = store;

    public WorkbenchPlacement? Load()
    {
        var text = store.ReadText(FileName, 4096);
        return text is null ? null : WorkbenchPlacement.Parse(text);
    }

    public void Save(WorkbenchPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        store.WriteText(FileName, placement.Serialize());
    }
}
