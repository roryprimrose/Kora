using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardInspectionAndDisableReadExactRetainedMemoryWithoutRenewingAuthority(bool profile)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new MemoryAnswerAdapter();
            Guid id;
            long disabledRevision;
            var value = string.Join(" ", Enumerable.Repeat("Private reviewed preference.", 12)) + " Exact final value.";
            await using (var composition = new Composition(f, adapter: adapter, memoryContext: true))
            {
                var model = composition.Model;
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.DeviceProfileMemory = profile;
                model.MemoryClass = MemoryContentClass.ResponsePreference;
                model.MemoryDraft = value;
                await model.ProposeMemoryAsync();
                model.SelectMemory(model.MemoryRecords.Single());
                await model.InspectMemoryAsync();
                await model.ReviewMemoryAsync(true);
                await model.AdmitMemoryAsync();
                id = model.SelectedMemory!.Id;
                var enabledRevision = model.SelectedMemory.Revision;
                var created = model.SelectedMemory.CreatedAt;
                var session = model.SelectedSessionRecord!.Authority.SessionId;
                var retention = await f.Store.ReadRetentionAsync(session, f.Token);
                model.DeviceProfileMemory = false;
                var window = new SessionsWindow(model) { Width = 900, Height = 700 };
                window.Show();
                try
                {
                    var tab = window.GetLogicalDescendants().OfType<TabItem>().Single(item => Equals(item.Header, "Memories"));
                    tab.GetLogicalAncestors().OfType<TabControl>().First().SelectedItem = tab;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var scope = tab.GetLogicalDescendants().OfType<CheckBox>().Single(control =>
                        AutomationProperties.GetName(control)?.StartsWith("Choose Device-profile memory", StringComparison.Ordinal) == true);
                    scope.Focus().Should().BeTrue();
                    if (profile)
                    {
                        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    }
                    model.DeviceProfileMemory.Should().Be(profile);
                    await Press("ListMemories");
                    var clear = window.FindControl<Button>("ClearMemoryFilters")!;
                    clear.BringIntoView();
                    clear.Focus().Should().BeTrue();
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    model.SelectedMemory!.Id.Should().Be(id);
                    model.CanDisableMemory.Should().BeFalse("selection alone must not admit a mutation");
                    await Press("InspectMemory");
                    var reader = window.FindControl<TextBox>("MemoryDetailReader");
                    reader.Should().NotBeNull("the complete inspection needs a keyboard-readable native value control");
                    reader!.IsFocused.Should().BeTrue("inspection must hand off to content, never to an affirmative action");
                    reader.IsReadOnly.Should().BeTrue();
                    reader.IsUndoEnabled.Should().BeFalse();
                    reader.CaretIndex.Should().Be(0);
                    AutomationProperties.GetName(reader).Should().Contain("Exact memory");
                    var peer = ControlAutomationPeer.CreatePeerForElement(reader)!;
                    peer.GetAutomationControlType().Should().Be(AutomationControlType.Edit);
                    var accessible = peer.GetProvider<IValueProvider>()!;
                    accessible.IsReadOnly.Should().BeTrue();
                    accessible.Value.Should().Be(model.MemoryDetail);
                    using (var inspection = JsonDocument.Parse(accessible.Value))
                    {
                        var exact = inspection.RootElement.GetProperty("inspected");
                        exact.GetProperty("candidate").GetProperty("value").GetString().Should().Be(value);
                        exact.GetProperty("candidate").GetProperty("contentClass").GetString().Should().Be("ResponsePreference");
                        exact.GetProperty("scope").GetProperty("kind").GetString().Should().Be(profile ? "DeviceProfile" : "Session");
                    }
                    window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    model.SelectedMemory.Revision.Should().Be(enabledRevision);
                    model.SelectedMemory.Retention.Should().Be(MemoryRetentionState.Enabled);
                    var disable = window.FindControl<Button>("DisableMemory")!;
                    window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                    window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                    disable.IsFocused.Should().BeTrue("the deliberate disable action remains reachable from complete inspection");
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    await window.MemoryOperation;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    reader.IsFocused.Should().BeTrue();
                    reader.CaretIndex.Should().Be(0);
                    accessible.Value.Should().Be(model.MemoryDetail).And.NotContain(value);
                    using (var result = JsonDocument.Parse(accessible.Value))
                    {
                        result.RootElement.GetProperty("outcome").GetString().Should().Be("Succeeded");
                        var exact = result.RootElement.GetProperty("memories")[0];
                        exact.GetProperty("id").GetGuid().Should().Be(id);
                        exact.GetProperty("scope").GetProperty("kind").GetString().Should().Be(profile ? "DeviceProfile" : "Session");
                        exact.GetProperty("retention").GetString().Should().Be("Disabled");
                    }
                    model.SelectedMemory.Id.Should().Be(id);
                    disabledRevision = model.SelectedMemory.Revision;
                    disabledRevision.Should().Be(enabledRevision + 1);
                    model.SelectedMemory.CreatedAt.Should().Be(created);
                    model.CanDisableMemory.Should().BeFalse();
                    model.CanAdmitMemory.Should().BeFalse("reading a retained result never re-enables it");
                    (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
                    f.Count("reviewed_memory").Should().Be(1);
                    f.Count("conversation_messages").Should().Be(0);
                    f.Count("scoped_grants").Should().Be(0);
                    adapter.Calls.Should().Be(0);
                    (await f.Tasks.ReadIncompleteAsync(100, f.Token)).Should().BeEmpty();

                    async Task Press(string name)
                    {
                        var button = window.FindControl<Button>(name)!;
                        button.IsEnabled.Should().BeTrue(model.Status);
                        button.BringIntoView();
                        button.Focus().Should().BeTrue();
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                        await window.MemoryOperation;
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                    }
                }
                finally { window.ClosePrivate(); }
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            await using var reopened = new Composition(f, memoryContext: true);
            await reopened.Model.RefreshAsync();
            await reopened.Model.SelectAsync(reopened.Model.Sessions.Single());
            reopened.Model.DeviceProfileMemory = profile;
            await reopened.Model.ListMemoriesAsync();
            var retained = reopened.Model.MemoryRecords.Single();
            retained.Id.Should().Be(id);
            retained.Revision.Should().Be(disabledRevision);
            retained.Retention.Should().Be(MemoryRetentionState.Disabled);
            reopened.Model.SelectMemory(retained);
            await reopened.Model.InspectMemoryAsync();
            reopened.Model.MemoryDetail.Should().Contain("Disabled").And.Contain("Exact final value.");
            reopened.Model.CanAdmitMemory.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(false, "selection")]
    [InlineData(true, "selection")]
    [InlineData(false, "scope")]
    [InlineData(true, "scope")]
    [InlineData(false, "control")]
    [InlineData(true, "control")]
    [InlineData(false, "tab")]
    [InlineData(true, "tab")]
    [InlineData(false, "parent")]
    [InlineData(true, "parent")]
    [InlineData(false, "hide")]
    [InlineData(true, "hide")]
    [InlineData(false, "activation")]
    [InlineData(true, "activation")]
    public async Task MemoryResultHandoffRechecksSourceAndSurfaceAfterNativeScroll(bool disable, string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new Composition(f, memoryContext: true);
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.MemoryDraft = "Keep the exact admitted preference";
            await model.ProposeMemoryAsync();
            model.SelectMemory(model.MemoryRecords.Single());
            await model.InspectMemoryAsync();
            await model.ReviewMemoryAsync(true);
            await model.AdmitMemoryAsync();
            if (disable) { await model.InspectMemoryAsync(); }
            var original = model.SelectedMemory!;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                var tab = window.GetLogicalDescendants().OfType<TabItem>().Single(item => Equals(item.Header, "Memories"));
                var tabs = tab.GetLogicalAncestors().OfType<TabControl>().First();
                tabs.SelectedItem = tab;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var reader = window.FindControl<TextBox>("MemoryDetailReader")!;
                var sentinel = window.FindControl<TextBox>("SessionMemoryCopyStatus")!;
                var observed = false;
                reader.AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
                {
                    if (observed) { return; }
                    observed = true;
                    switch (change)
                    {
                        case "selection":
                            model.SelectMemory(null);
                            break;
                        case "scope":
                            model.DeviceProfileMemory = true;
                            break;
                        case "control":
                            composition.Access.ControlRevision++;
                            break;
                        case "tab":
                            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(item => !ReferenceEquals(item, tab));
                            window.UpdateLayout();
                            tabs.SelectedItem = tab;
                            window.UpdateLayout();
                            break;
                        case "parent":
                            var parent = reader.GetVisualAncestors().OfType<ScrollViewer>().First();
                            parent.IsVisible = false;
                            parent.IsVisible = true;
                            break;
                        case "hide":
                            window.Hide();
                            window.Show();
                            break;
                        case "activation":
                            HeadlessSession.SetActivation(window, false);
                            HeadlessSession.SetActivation(window, true);
                            break;
                        default:
                            throw new InvalidOperationException("Unknown memory presentation transition.");
                    }
                    sentinel.Focus().Should().BeTrue();
                });
                var button = window.FindControl<Button>(disable ? "DisableMemory" : "InspectMemory")!;
                button.BringIntoView();
                button.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                await window.MemoryOperation;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                observed.Should().BeTrue("the real native result scroll must exercise the final presentation fence");
                reader.IsFocused.Should().BeFalse("returning to the same surface cannot renew retired focus");
                sentinel.IsFocused.Should().BeTrue();
                model.DeviceProfileMemory = false;
                await model.ListMemoriesAsync();
                var retained = model.MemoryRecords.Single();
                retained.Id.Should().Be(original.Id);
                retained.Revision.Should().Be(original.Revision + (disable ? 1 : 0));
                retained.Retention.Should().Be(disable ? MemoryRetentionState.Disabled : MemoryRetentionState.Enabled);
                f.Count("reviewed_memory").Should().Be(1);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { window.ClosePrivate(); }
        });
    }
}
