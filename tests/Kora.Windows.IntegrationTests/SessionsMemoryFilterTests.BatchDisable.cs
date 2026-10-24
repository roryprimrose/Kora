using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsMemoryFilterTests
{
    [WindowsFact]
    public async Task BatchReviewShowsCompleteExactSubsetAndCountBeforeSeparateOneShotDisable()
    {
        await using var f = await Fixture.Create();
        for (var index = 0; index < 3; index++) { await f.CreateMemory("enabled"); }
        await f.Model.ListMemoriesAsync();
        var selection = f.Model.MemoryRecords.Take(2).ToArray();
        var calls = f.Store.Calls;
        f.Model.SelectMemoryBatch(selection);
        f.Store.Calls.Should().Be(calls);
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
        await f.Model.ReviewMemoryBatchAsync();
        f.Model.CanConfirmMemoryBatch.Should().BeTrue();
        f.Model.MemoryBatchDetail.Should().Contain("Review 2 exact").And.Contain("private exact value")
            .And.Contain("Decision").And.Contain("not atomic-all").And.Contain("cannot be rolled back");
        foreach (var row in selection)
        {
            f.Model.MemoryBatchDetail.Should().Contain(row.Id.ToString("D"));
        }
        f.Model.MemoryBatchDetail.Should().NotContain(f.Model.MemoryRecords[2].Id.ToString("D"));
        f.Model.MemoryRecords.Should().OnlyContain(row => row.Retention == MemoryRetentionState.Enabled);
        await f.Model.ConfirmMemoryBatchAsync();
        f.Model.MemoryRecords.Count(row => row.Retention == MemoryRetentionState.Disabled).Should().Be(2);
        f.Model.MemoryRecords.Count(row => row.Retention == MemoryRetentionState.Enabled).Should().Be(1);
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
        f.Model.MemoryBatchSelection.Should().BeEmpty();
        f.Model.MemoryBatchDetail.Should().Contain("Succeeded").And.NotContain("private exact value");
        f.Logger.Messages.Should().NotContain(message => message.Contains("private exact value", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("single")]
    [InlineData("filter")]
    [InlineData("draft")]
    [InlineData("classification")]
    [InlineData("cancel")]
    [InlineData("control")]
    [InlineData("close")]
    public async Task BatchReceiptRetiresOnEveryMemoryEditAndPrivatePresentationBoundary(string boundary)
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        var row = f.Model.MemoryRecords.Single();
        f.Model.SelectMemoryBatch([row]);
        await f.Model.ReviewMemoryBatchAsync();
        f.Model.CanConfirmMemoryBatch.Should().BeTrue();
        var calls = f.Store.Calls;
        switch (boundary)
        {
            case "selection": f.Model.SelectMemoryBatch([row]); break;
            case "single": f.Model.SelectMemory(row); break;
            case "filter": f.Model.MemoryIdFilter = row.Id.ToString("D"); break;
            case "draft": f.Model.MemoryDraft = "edited proposal"; break;
            case "classification": f.Model.MemoryClass = MemoryContentClass.WorkflowPreference; break;
            case "cancel": f.Model.CancelMemoryBatch(); break;
            case "control": f.Access.ControlRevision++; break;
            case "close": f.Model.Close(); break;
        }
        f.Store.Calls.Should().Be(calls);
        f.Model.CanConfirmMemoryBatch.Should().BeFalse();
        f.Model.MemoryBatchDetail.Should().BeEmpty();
        f.Model.CanDisableMemory.Should().BeFalse();
    }

    [WindowsFact]
    public async Task CancelDuringPostCommitPreservesSucceededAndNeverAttemptsTheNextRecord()
    {
        await using var f = await Fixture.Create();
        await f.CreateMemory("enabled");
        await f.CreateMemory("enabled");
        await f.Model.ListMemoriesAsync();
        var selection = f.Model.MemoryRecords.ToArray();
        f.Model.SelectMemoryBatch(selection);
        await f.Model.ReviewMemoryBatchAsync();
        f.Store.AfterTransaction = result =>
        {
            if (result.Record?.Retention == MemoryRetentionState.Disabled) { f.Model.CancelMemoryBatch(); }
        };
        await f.Model.ConfirmMemoryBatchAsync();
        f.Model.MemoryBatchDetail.Should().Contain(selection[0].Id + " reviewed revision")
            .And.Contain("Succeeded").And.Contain("NotAttempted").And.NotContain("private exact value");
        f.Model.MemoryRecords[0].Retention.Should().Be(MemoryRetentionState.Disabled);
        f.Model.MemoryRecords[1].Retention.Should().Be(MemoryRetentionState.Enabled);
    }

    [WindowsFact]
    public async Task NativeBatchControlsSupportKeyboardSubsetReviewCancelAndDirectResults()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.Create();
            for (var index = 0; index < 3; index++) { await f.CreateMemory("enabled"); }
            await f.Model.ListMemoriesAsync();
            var window = new SessionsWindow(f.Model);
            window.Show();
            try
            {
                var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab =>
                    string.Equals(tab.Header?.ToString(), "Session memories", StringComparison.Ordinal));
                window.Activate();
                var list = window.FindControl<ListBox>("MemoryBatchRecords")!;
                var review = window.FindControl<Button>("ReviewMemoryBatch")!;
                var confirm = window.FindControl<Button>("ConfirmMemoryBatch")!;
                var cancel = window.FindControl<Button>("CancelMemoryBatch")!;
                var detail = window.FindControl<TextBox>("MemoryBatchDetail")!;
                list.SelectionMode.Should().Be(SelectionMode.Multiple);
                detail.IsReadOnly.Should().BeTrue();
                detail.AcceptsReturn.Should().BeTrue();
                foreach (var control in new Control[] { list, review, confirm, cancel, detail })
                {
                    Avalonia.Automation.AutomationProperties.GetName(control).Should().NotBeNullOrWhiteSpace();
                }
                var calls = f.Store.Calls;
                list.SelectedIndex = 0;
                list.BringIntoView();
                window.UpdateLayout();
                var first = (ListBoxItem)list.ContainerFromIndex(0)!;
                first.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
                list.SelectedItems!.Count.Should().Be(2);
                f.Model.MemoryBatchSelection.Should().HaveCount(2);
                f.Store.Calls.Should().Be(calls);
                confirm.IsEnabled.Should().BeFalse();
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.MemoryBatchOperation;
                detail.Text.Should().Contain("Review 2 exact").And.Contain("private exact value").And.Contain("Decision");
                confirm.IsEnabled.Should().BeTrue();
                first.Focus().Should().BeTrue();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                confirm.IsEnabled.Should().BeFalse();
                detail.Text.Should().BeEmpty();
                list.SelectedItems!.Count.Should().Be(0);
                list.SelectedIndex = 1;
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.MemoryBatchOperation;
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.MemoryBatchOperation;
                detail.Text.Should().Contain("Succeeded").And.NotContain("private exact value");
                f.Model.MemoryRecords.Count(row => row.Retention == MemoryRetentionState.Disabled).Should().Be(1);
                confirm.IsEnabled.Should().BeFalse();
            }
            finally { window.ClosePrivate(); }
        });
    }
}
