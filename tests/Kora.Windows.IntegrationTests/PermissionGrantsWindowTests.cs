using Avalonia.Automation.Peers;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Interaction;
using Kora.Controls;
using Kora.Core.Auditing;
using Kora.Core.Network;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PermissionGrantsWindowTests
{
    [WindowsFact]
    public async Task NativeSharedInventoryGroupsPassivelyAndRoutesUriEditsWithoutExactGrantAuthority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var grant = await f.GrantAsync("perpetual");
            var preferences = new Preferences { Settings = PreapprovedUriSettings.Create(["https://example.com/*"]) };
            var policy = new PreapprovedUriConfigurationService(preferences, new Audit());
            await using var control = new ExactGrantControlAdmission(f.Store, f.Store, new(f.Tasks), f.Authorization);
            using var model = new ExactGrantsViewModel(f.Store, control, new WindowsSqliteSessionWorkspaceTests.Access(),
                () => true, NullLogger<ExactGrantsViewModel>.Instance, new(f.Store, policy), () => true);
            var window = new ExactGrantsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync(f.Token);
                var list = window.FindControl<ScrollableListBox>("PermissionInventory")!;
                list.ItemCount.Should().Be(2);
                var uriRow = model.PermissionRows.Single(row => row.Grant is UriPermissionGrant);
                list.SelectedItem = uriRow;
                model.SelectedPermission.Should().BeSameAs(uriRow);
                model.Selected.Should().BeNull();
                model.CanRemoveUriRule.Should().BeTrue();
                model.PreviewBatchCommand.CanExecute(null).Should().BeFalse();
                var events = f.Count("security_audit_events");
                var grouping = window.FindControl<ComboBox>("PermissionGrouping")!;
                ControlAutomationPeer.CreatePeerForElement(grouping)!.GetName().Should().Contain("no read or group selection");
                grouping.SelectedItem = "Artefact type";
                model.PermissionRows.Select(row => row.GroupHeading).Should().Equal("URI pattern (1)", "Unknown (1)");
                list.SelectedItem.Should().BeNull();
                model.CanRemoveUriRule.Should().BeFalse();
                f.Count("security_audit_events").Should().Be(events);
                preferences.Saves.Should().Be(0);
                list.SelectedItem = model.PermissionRows.Single(row => row.Grant is UriPermissionGrant);
                var remove = window.FindControl<Button>("RemoveUriPermission")!;
                remove.IsEnabled.Should().BeTrue();
                await ((AsyncCommand)remove.Command!).ExecuteAsync();
                policy.IsPreapproved(new("https://example.com/page")).Should().BeFalse();
                list.ItemCount.Should().Be(1);
                var input = window.FindControl<TextBox>("UriPermissionPattern")!;
                input.Text = "https://new.example/*";
                var add = window.FindControl<Button>("AddUriPermission")!;
                add.IsEnabled.Should().BeTrue();
                await ((AsyncCommand)add.Command!).ExecuteAsync();
                policy.IsPreapproved(new("https://new.example/page")).Should().BeTrue();
                list.ItemCount.Should().Be(2);
                (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(grant);
            }
            finally { window.Close(); }
        });
    }

    private sealed class Preferences : IPreapprovedUriPreferences
    {
        internal PreapprovedUriSettings Settings { get; set; } = PreapprovedUriSettings.Empty;
        internal int Saves { get; private set; }
        public PreapprovedUriSettings Load() => Settings;
        public void Save(PreapprovedUriSettings settings) { Settings = settings; Saves++; }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
