using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class HostedPermissionGrantsTests
{
    [Fact]
    public async Task Permissions_use_one_shell_and_retire_private_records_on_deactivation_without_revocation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."), exactGrants: true);
            await fixture.InitializeAsync();
            using var response = new ResponseWindowController(fixture.Main, NullLogger<ResponseWindowController>.Instance);
            var window = response.PrepareWorkbenchOwner();
            using var permissions = new ExactGrantsWindowController(fixture.Main, fixture.Interactions,
                fixture.ExactGrantControl!, fixture.Access, () => true, NullLogger<ExactGrantsViewModel>.Instance,
                new PermissionGrantManagementService(fixture.Interactions));
            permissions.BindWorkbench(() => window, view => window.ShowHostedView(WorkbenchActivity.Permissions, view));
            var separateWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<ExactGrantsWindow>((_, _) => separateWindows++);
            permissions.Open();
            await permissions.InventoryOperation;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.ActiveActivity.Should().Be(WorkbenchActivity.Permissions);
            var view = (ExactGrantsView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            var model = (ExactGrantsViewModel)view.DataContext!;
            if (model.Records.IsEmpty)
            {
                model.NextCommand.CanExecute(null).Should().BeTrue("the fixture contains one perpetual grant in at most two bounded pages");
                await model.NextAsync(fixture.Token);
            }
            model.Records.Should().ContainSingle();
            var original = model.Records[0].Grant;
            model.Selected = model.Records[0];
            await model.InspectAsync(fixture.Token);
            model.Reviewed = true;
            model.Inspection.Should().Contain(original.Id.Value.ToString("D"));
            window.ShowSessions(false, activate: false);
            model.Records.Should().BeEmpty();
            model.Reviewed.Should().BeFalse();
            model.Inspection.Should().NotContain(original.Id.Value.ToString("D"));
            (await fixture.Interactions.InspectExactGrantAsync(original.Id, fixture.Token))!.Grant.Should().Be(original);
            separateWindows.Should().Be(0);
            permissions.Open();
            window.ActiveActivity.Should().Be(WorkbenchActivity.Permissions);
            model.Reviewed.Should().BeFalse();
        });
    }
}
