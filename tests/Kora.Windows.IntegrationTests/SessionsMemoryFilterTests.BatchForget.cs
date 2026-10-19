using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsMemoryFilterTests
{
    [WindowsFact]
    public async Task NativeForgetReviewListsCompleteMixedSubsetAndSeparateConfirmationRemovesOnlyTheirBodies()
    {
        await using var f = await Fixture.Create();
        var first = await f.CreateMemory("enabled");
        var second = await f.CreateMemory("disabled");
        var unselected = await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        var selected = f.Model.MemoryRecords.Where(row => row.Id == first.Id || row.Id == second.Id).ToArray();
        f.Model.MemoryBatchOperation = MemoryBatchOperation.Forget;
        f.Model.SelectMemoryBatch(selected);
        var calls = f.Store.Calls;
        f.Model.MemoryBatchConfirmLabel.Should().Be("Confirm sequential FORGET");
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
        f.Store.Calls.Should().Be(calls);
        await f.Model.ReviewMemoryBatchAsync();
        f.Model.MemoryBatchDetail.Should().Contain("Review 2 exact").And.Contain("FORGET only")
            .And.Contain("private exact value").And.Contain("Decision").And.Contain("Irreversible")
            .And.Contain("physical or forensic").And.Contain("not atomic-all");
        foreach (var row in selected)
        {
            f.Model.MemoryBatchDetail.Should().Contain(row.Id.ToString("D"));
        }
        f.Model.MemoryBatchDetail.Should().NotContain(unselected.Id.ToString("D"));
        await f.Model.ConfirmMemoryBatchAsync();
        f.Model.MemoryRecords.Count(row => row.Retention == MemoryRetentionState.Forgotten).Should().Be(2);
        f.Model.MemoryRecords.Single(row => row.Id == unselected.Id).Retention.Should().Be(MemoryRetentionState.Enabled);
        f.Model.MemoryBatchDetail.Should().Contain("Forget results").And.Contain("Succeeded")
            .And.NotContain("private exact value");
        f.Model.MemoryDetail.Should().BeEmpty();
        f.Model.MemoryDraft.Should().BeEmpty();
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
        f.Model.MemoryBatchSelection.Should().BeEmpty();
        f.Logger.Messages.Should().NotContain(message => message.Contains("private exact value", StringComparison.Ordinal));
        var result = await f.Management.ExecuteAsync(new(MemoryCommandOperation.Inspect, f.Storage.Request.SessionId.Value,
            selected[0].Id, selected[0].Revision + 1), RequestOrigin.LocalUi, () => true, f.Storage.Token);
        result.Inspected!.Candidate.Should().BeNull();
        result.Inspected.Receipt.Should().BeNull();
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("selection")]
    [InlineData("single")]
    [InlineData("filter")]
    [InlineData("draft")]
    [InlineData("classification")]
    [InlineData("cancel")]
    [InlineData("list")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("close")]
    public async Task ForgetReceiptCannotSurviveAnySelectionEditOperationOrPrivateSourceBoundary(string boundary)
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.MemoryBatchOperation = MemoryBatchOperation.Forget;
        f.Model.SelectMemoryBatch([row]);
        await f.Model.ReviewMemoryBatchAsync();
        var calls = f.Store.Calls;
        switch (boundary)
        {
            case "operation": f.Model.MemoryBatchOperation = MemoryBatchOperation.Disable; break;
            case "selection": f.Model.SelectMemoryBatch([row]); break;
            case "single": f.Model.SelectMemory(row); break;
            case "filter": f.Model.MemoryIdFilter = row.Id.ToString("D"); break;
            case "draft": f.Model.MemoryDraft = "new private draft"; break;
            case "classification": f.Model.MemoryClass = MemoryContentClass.WorkflowPreference; break;
            case "cancel": f.Model.CancelMemoryBatch(); f.Model.CancelMemoryBatch(); break;
            case "list": await f.Model.ListMemoriesAsync(); break;
            case "privacy": f.Access.CanControl = false; break;
            case "control": f.Access.ControlRevision++; break;
            case "close": f.Model.Close(); break;
        }
        if (boundary is not "list") { f.Store.Calls.Should().Be(calls); }
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
        f.Model.MemoryBatchDetail.Should().BeEmpty();
        f.Model.MemoryDetail.Should().BeEmpty();
        f.Model.MemoryDraft.Should().Be(boundary is "draft" ? "new private draft" : string.Empty);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("close")]
    public async Task ForgetStopsAfterFirstCommittedActionAndNeverPresentsRemovedOrPrivateContent(string boundary)
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        f.Model.MemoryBatchOperation = MemoryBatchOperation.Forget;
        f.Model.SelectMemoryBatch(f.Model.MemoryRecords.ToArray());
        await f.Model.ReviewMemoryBatchAsync();
        f.Store.AfterTransaction = result =>
        {
            if (result.Record?.Retention != MemoryRetentionState.Forgotten) { return; }
            switch (boundary)
            {
                case "cancel": f.Model.CancelMemoryBatch(); f.Model.CancelMemoryBatch(); break;
                case "privacy": f.Access.CanControl = false; break;
                case "control": f.Access.ControlRevision++; break;
                case "close": f.Model.Close(); break;
            }
        };
        await f.Model.ConfirmMemoryBatchAsync();
        if (boundary is "cancel")
        {
            f.Model.MemoryRecords[0].Retention.Should().Be(MemoryRetentionState.Forgotten);
            f.Model.MemoryRecords[1].Retention.Should().Be(MemoryRetentionState.Enabled);
            f.Model.MemoryBatchDetail.Should().Contain("Succeeded").And.Contain("NotAttempted")
                .And.NotContain("private exact value");
        }
        else { f.Model.MemoryBatchDetail.Should().BeEmpty(); }
        f.Model.MemoryDetail.Should().BeEmpty();
        f.Model.MemoryDraft.Should().BeEmpty();
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
    }

    [WindowsFact]
    public async Task ReentrantConfirmDuringReviewNotificationRetiresTheReviewWithoutAForget()
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        f.Model.MemoryBatchOperation = MemoryBatchOperation.Forget;
        f.Model.SelectMemoryBatch(f.Model.MemoryRecords.ToArray());
        f.Model.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(SessionsViewModel.MemoryBatchDetail), StringComparison.Ordinal)
                && f.Model.MemoryBatchDetail.Contains("FORGET only", StringComparison.Ordinal))
            {
                f.Model.ConfirmMemoryBatchAsync().IsCompletedSuccessfully.Should().BeTrue();
            }
        };
        await f.Model.ReviewMemoryBatchAsync();
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
        f.Model.MemoryBatchDetail.Should().BeEmpty();
        f.Model.MemoryRecords.Should().OnlyContain(row => row.Retention == MemoryRetentionState.Enabled);
    }

    [WindowsFact]
    public async Task NativeKeyboardAccessibleOperationChoiceBindsTruthfulForgetLabelsAndCancelClearsBodies()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            await f.CreateMemory("enabled");
            await f.Model.ListMemoriesAsync();
            var window = new SessionsWindow(f.Model);
            window.Show();
            try
            {
                var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab =>
                    string.Equals(tab.Header?.ToString(), "Session memories", StringComparison.Ordinal));
                var choice = window.FindControl<ComboBox>("MemoryBatchOperationChoice")!;
                var list = window.FindControl<ListBox>("MemoryBatchRecords")!;
                var review = window.FindControl<Button>("ReviewMemoryBatch")!;
                var confirm = window.FindControl<Button>("ConfirmMemoryBatch")!;
                var cancel = window.FindControl<Button>("CancelMemoryBatch")!;
                var detail = window.FindControl<TextBox>("MemoryBatchDetail")!;
                Avalonia.Automation.AutomationProperties.GetName(choice).Should().Contain("irreversible Forget");
                choice.SelectedItem = MemoryBatchOperation.Forget;
                list.SelectedIndex = 0;
                review.Content.Should().Be("Review selected forget subset");
                confirm.Content.Should().Be("Confirm sequential FORGET");
                confirm.IsEnabled.Should().BeFalse();
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.MemoryBatchOperation;
                detail.Text.Should().Contain("FORGET only").And.Contain("private exact value");
                confirm.IsEnabled.Should().BeTrue();
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                detail.Text.Should().BeEmpty();
                confirm.IsEnabled.Should().BeFalse();
                list.SelectedItems!.Count.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }
}
