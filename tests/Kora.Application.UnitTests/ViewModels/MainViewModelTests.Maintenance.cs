using AwesomeAssertions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public void Maintenance_entry_projects_passive_status_without_changing_existing_version_question()
    {
        var fixture = new Fixture();
        fixture.ViewModel.MaintenanceStatus.Should().Contain("Unknown");
        fixture.ViewModel.ShowMaintenance();
        var opened = 0;
        fixture.ViewModel.MaintenanceRequested += (_, _) => opened++;
        fixture.ViewModel.ShowMaintenance();
        opened.Should().Be(1);
        fixture.ViewModel.MaintenanceStatus = "Available: unsigned canonical metadata";
        fixture.ViewModel.MaintenanceStatus.Should().Contain("Available");
    }
}
