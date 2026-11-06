using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Mcp;
using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed class McpCapabilityManagerTests
{
    [Fact]
    public async Task ActiveFilterDoesNotNarrowCategoryBulkChangesOrDiscardIndividualDraftChoices()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var changes = new Changes();
                var inventory = Inventory();
                var pending = McpCapabilityManagerWindow.ShowAsync(owner, changes, inventory,
                    () => changes.Current, TestContext.Current.CancellationToken);
                var dialog = owner.OwnedWindows.Should().ContainSingle().Subject.Should().BeOfType<McpCapabilityManagerWindow>().Subject;
                var checks = dialog.GetLogicalDescendants().OfType<CheckBox>().ToArray();
                var resource = checks.Single(check => Equals(check.Content, "Resource: Reference"));
                var template = checks.Single(check => Equals(check.Content, "ResourceTemplate: Files"));
                dialog.FilterCapabilities("data://item", McpCapabilityCategory.Resources);
                dialog.CapabilityFilterSummary.Text.Should().Be("1 of 2 capabilities");
                resource.IsVisible.Should().BeTrue();
                template.IsVisible.Should().BeFalse();
                Click(dialog, "Disable all Resources");
                resource.IsChecked.Should().BeFalse();
                template.IsChecked.Should().BeFalse();
                resource.IsChecked = true;
                dialog.FilterCapabilities("no matches", McpCapabilityCategory.Resources);
                dialog.CapabilityFilterSummary.Text.Should().Be("0 of 2 capabilities");
                dialog.ClearCapabilityFilter();
                dialog.CapabilityFilterSummary.Text.Should().Be("2 of 2 capabilities");
                resource.IsChecked.Should().BeTrue();
                template.IsChecked.Should().BeFalse();
                pending.IsCompleted.Should().BeFalse();
                Click(dialog, "Review changes");
                Click(dialog, "Confirm Save of these preferences");
                var saved = await pending;
                saved!.Selection.IsEnabled(inventory.ServerId, McpCapabilityKind.Resource, "data://item").Should().BeTrue();
                saved.Selection.IsEnabled(inventory.ServerId, McpCapabilityKind.ResourceTemplate, "data://files/{value}").Should().BeFalse();
                saved.Selection.IsEnabled(inventory.ServerId, McpCapabilityKind.Resource, "data://future").Should().BeFalse();
                inventory.Selection.Categories.Should().BeEmpty();
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public async Task NativeCategoryAndItemChangesAreDraftsUntilSeparateConfirmationAndServerDisablePreservesThem()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var changes = new Changes();
                var inventory = Inventory();
                var pending = McpCapabilityManagerWindow.ShowAsync(owner, changes, inventory, () => changes.Current, TestContext.Current.CancellationToken);
                var dialog = owner.OwnedWindows.Single();
                var checkboxes = dialog.GetLogicalDescendants().OfType<CheckBox>().ToArray();
                checkboxes.Should().HaveCount(5);
                checkboxes.Should().OnlyContain(check => check.IsChecked == true);
                Click(dialog, "Disable all Resources");
                checkboxes.Single(check => Equals(check.Content, "Resource: Reference")).IsChecked.Should().BeFalse();
                checkboxes.Single(check => Equals(check.Content, "ResourceTemplate: Files")).IsChecked.Should().BeFalse();
                checkboxes.Single(check => Equals(check.Content, "Resource: Reference")).IsChecked = true;
                dialog.GetLogicalDescendants().OfType<TextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.StartsWith("Resources: mixed", StringComparison.Ordinal));
                checkboxes.Single(check => Equals(check.Content, "Server enabled in Kora")).IsChecked = false;
                pending.IsCompleted.Should().BeFalse();
                Click(dialog, "Review changes");
                pending.IsCompleted.Should().BeFalse();
                dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text.Should().Contain("Complete discovered item selections");
                Click(dialog, "Confirm Save of these preferences");
                var result = await pending;
                result!.ServerIncluded.Should().BeFalse();
                result.Selection.IsEnabled(inventory.ServerId, McpCapabilityKind.Resource, "data://item").Should().BeTrue();
                result.Selection.IsEnabled(inventory.ServerId, McpCapabilityKind.ResourceTemplate, "data://files/{value}").Should().BeFalse();
                result.Selection.IsEnabled(inventory.ServerId, McpCapabilityKind.Resource, "data://new").Should().BeFalse();
                result.Selection.IsEnabled(inventory.ServerId, McpCapabilityKind.Tool, "search").Should().BeTrue();
                inventory.Selection.Categories.Should().BeEmpty();
                changes.Subscribers.Should().Be(0);
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivacyWithdrawalOrCancellationClosesAndReleasesThePrivateManagerWithoutAChange(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var changes = new Changes();
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                var pending = McpCapabilityManagerWindow.ShowAsync(owner, changes, Inventory(), () => changes.Current, cancellation.Token);
                owner.OwnedWindows.Should().ContainSingle();
                if (cancel)
                {
                    await cancellation.CancelAsync();
                    await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
                }
                else
                {
                    changes.Current = false;
                    changes.Notify();
                    (await pending).Should().BeNull();
                }
                owner.OwnedWindows.Should().BeEmpty();
                changes.Subscribers.Should().Be(0);
            }
            finally { owner.Close(); }
        });
    }

    private static void Click(Window dialog, string text) => dialog.GetLogicalDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static McpCapabilityInventory Inventory()
    {
        var serverId = new string('a', 64);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        var source = new McpConnectionDescriptor(serverId, "Reference server", Path.GetFullPath("fixture.json"),
            new string('b', 64), account.Destination, account, 0, CustomizationScope.Kora, 1);
        return new(null!, request, new SessionWorkspaceEntry(new(request.SessionId, new(1), true), null), source,
            [
                new(McpCapabilityKind.Tool, "search", "Search", "{}"),
                new(McpCapabilityKind.Resource, "data://item", "Reference", "{}"),
                new(McpCapabilityKind.ResourceTemplate, "data://files/{value}", "Files", "{}"),
                new(McpCapabilityKind.Prompt, "review", "Review", "{}"),
            ], McpCapabilitySelection.Default, 0, DateTimeOffset.UtcNow.AddMinutes(2), new(true, true, true));
    }

    private sealed class Changes : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? changed;
        internal bool Current { get; set; } = true;
        internal int Subscribers { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { changed += value; Subscribers++; }
            remove { changed -= value; Subscribers--; }
        }
        internal void Notify() => changed?.Invoke(this, new(nameof(Current)));
    }
}