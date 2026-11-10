namespace Kora.Core.Configuration;

public interface IWorkbenchPlacementPreferences
{
    WorkbenchPlacement? Load();
    void Save(WorkbenchPlacement placement);
}
