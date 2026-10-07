namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private string maintenanceStatus = "Unknown: canonical release metadata has not been checked.";
    public string MaintenanceStatus
    {
        get => maintenanceStatus;
        set => SetProperty(ref maintenanceStatus, value);
    }

    public event EventHandler? MaintenanceRequested;
    public void ShowMaintenance() => MaintenanceRequested?.Invoke(this, EventArgs.Empty);
}
