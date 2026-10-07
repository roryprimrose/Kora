using Avalonia.Controls;
using Kora.Application.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class MaintenanceWindow : Window
{
    public MaintenanceWindow() : this(App.Services.GetRequiredService<MaintenanceViewModel>()) { }

    public MaintenanceWindow(MaintenanceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
