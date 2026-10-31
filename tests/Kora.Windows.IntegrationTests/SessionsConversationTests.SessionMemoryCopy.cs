using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeSessionMemoryCopyCompletesActualButtonsFreshReviewRestartInspectAndSeparateLaterLocalUse()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            MemoryRecord original;
            Guid copyId;
            await using (var composition = new Composition(f, memoryContext: true))
            {
                var model = composition.Model;
                var window = new SessionsWindow(model);
                window.Show();
                try
                {
                    await model.RefreshAsync();
                    await model.SelectAsync(model.Sessions.Single());
                    await model.ReadConversationAsync();
                    model.MemoryClass = MemoryContentClass.ResponsePreference;
                    model.MemoryDraft = "Prefer explicit examples";
                    await model.ProposeMemoryAsync();
                    model.SelectMemory(model.MemoryRecords.Single());
                    await model.InspectMemoryAsync();
                    await model.ReviewMemoryAsync(true);
                    await model.AdmitMemoryAsync();
                    await model.InspectMemoryAsync();
                    var source = model.SelectedMemory!;
                    original = (await composition.Memories.ExecuteAsync(new(MemoryCommandOperation.Inspect,
                        f.Request.SessionId.Value, source.Id, source.Revision), RequestOrigin.LocalUi, () => true, f.Token)).Inspected!;
                    var memoriesTab = window.GetLogicalDescendants().OfType<TabItem>().Single(tab => Equals(tab.Header, "Memories"));
                    memoriesTab.GetLogicalAncestors().OfType<TabControl>().First().SelectedItem = memoriesTab;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var review = memoriesTab.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(field =>
                        AutomationProperties.GetName(field) is { } name
                        && name.StartsWith("Complete current Session source", StringComparison.Ordinal));
                    async Task Click(string name)
                    {
                        var button = window.FindControl<Button>(name)!;
                        button.IsEnabled.Should().BeTrue(model.ConversationStatus);
                        button.BringIntoView();
                        button.Focus().Should().BeTrue();
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                        await window.SessionMemoryCopyOperation;
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        if (!string.Equals(name, "AdmitSessionMemoryCopy", StringComparison.Ordinal))
                        {
                            review.IsFocused.Should().BeTrue("each fresh complete review must receive keyboard focus, not a decision button");
                            review.CaretIndex.Should().Be(0);
                        }
                    }
                    await Click("KeepSessionMemory");
                    model.SessionMemoryCopyDescription.Should().Contain("Prefer explicit examples").And.Contain("independent lifetime")
                        .And.Contain("does NOT delete").And.Contain("ResponsePreference");
                    model.SessionMemoryCopyClass.Should().BeNull("classification must not be inherited");
                    model.CanProposeSessionMemoryCopy.Should().BeFalse();
                    model.SessionMemoryCopyClass = MemoryContentClass.WorkflowPreference;
                    await Click("ProposeSessionMemoryCopy");
                    model.SessionMemoryCopyDescription.Should().Contain("WorkflowPreference").And.Contain("DeviceProfile")
                        .And.Contain("Proposed").And.Contain("CopiedFromSession");
                    model.CanAdmitSessionMemoryCopy.Should().BeFalse();
                    f.Count("reviewed_memory").Should().Be(1);
                    await Click("AcceptSessionMemoryCopy");
                    f.Count("reviewed_memory").Should().Be(1);
                    await Click("AdmitSessionMemoryCopy");
                    var outcome = window.FindControl<TextBox>("SessionMemoryCopyStatus")!;
                    outcome.IsFocused.Should().BeTrue("save completion must be readable without searching another part of the window");
                    outcome.Text.Should().Be(model.ConversationStatus);
                    model.ConversationStatus.Should().Contain("admitted independently");
                    model.SessionMemoryCopyDescription.Should().BeEmpty();
                    f.Count("reviewed_memory").Should().Be(2);
                    var unchanged = (await composition.Memories.ExecuteAsync(new(MemoryCommandOperation.Inspect,
                        f.Request.SessionId.Value, source.Id, source.Revision), RequestOrigin.LocalUi, () => true, f.Token)).Inspected;
                    unchanged.Should().Be(original);
                    model.DeviceProfileMemory = true;
                    await model.ListMemoriesAsync();
                    var copy = model.MemoryRecords.Single();
                    copyId = copy.Id;
                    copy.Id.Should().NotBe(source.Id);
                    model.SelectMemory(copy);
                    await model.InspectMemoryAsync();
                    model.MemoryDetail.Should().Contain("WorkflowPreference").And.Contain("copiedFromSession");
                    f.Count("conversation_messages").Should().Be(0);
                    f.Count("scoped_grants").Should().Be(0);
                }
                finally { window.ClosePrivate(); }
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var adapter = new MemoryAnswerAdapter();
            await using var later = new Composition(f, adapter: adapter, memoryContext: true);
            var next = await later.Workspace.CreateAsync(new("Later conversation"), RequestOrigin.LocalUi, f.Token);
            await later.Workspace.ChangeLifecycleAsync(original.Lineage.Request.SessionId,
                original.Lineage.SessionGeneration, false, RequestOrigin.LocalUi, f.Token);
            var viewer = later.Model;
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(row => row.Authority.SessionId == next.Authority.SessionId));
            await viewer.ReadConversationAsync();
            viewer.DeviceProfileMemory = true;
            await viewer.ListMemoriesAsync();
            var fresh = viewer.MemoryRecords.Single();
            fresh.Id.Should().Be(copyId);
            viewer.SelectMemory(fresh);
            await viewer.InspectMemoryAsync();
            viewer.MemoryDetail.Should().Contain("Prefer explicit examples").And.Contain("DeviceProfile");
            adapter.Calls.Should().Be(0);
            viewer.SelectMemoryContext([fresh]);
            viewer.MemoryContextRequest = "Explain this preference.";
            await viewer.ReviewMemoryContextAsync();
            adapter.Calls.Should().Be(0);
            await viewer.ConfirmMemoryContextAsync();
            adapter.Calls.Should().Be(1);
            await viewer.ConfirmMemoryContextAsync();
            adapter.Calls.Should().Be(1);
            viewer.Close();
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("source-forget")]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("control")]
    [InlineData("privacy")]
    public async Task NativeSessionMemoryCopyFocusReportsHeldOutcomeWithoutAdmissionOrLatePrivateFocus(string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            var checkpoint = new InteractionTransactionCheckpoint();
            f.Reopen(checkpoint);
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new Composition(f, memoryContext: true);
            var model = composition.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.MemoryDraft = "Prefer explicit examples";
                await model.ProposeMemoryAsync();
                model.SelectMemory(model.MemoryRecords.Single());
                await model.InspectMemoryAsync();
                await model.ReviewMemoryAsync(true);
                await model.AdmitMemoryAsync();
                await model.InspectMemoryAsync();
                var original = model.SelectedMemory!;
                var tab = window.GetLogicalDescendants().OfType<TabItem>().Single(item => Equals(item.Header, "Memories"));
                tab.GetLogicalAncestors().OfType<TabControl>().First().SelectedItem = tab;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                async Task Click(string name)
                {
                    var button = window.FindControl<Button>(name)!;
                    button.IsEnabled.Should().BeTrue(model.ConversationStatus);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.SessionMemoryCopyOperation;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }
                await Click("KeepSessionMemory");
                model.SessionMemoryCopyClass = MemoryContentClass.Decision;
                await Click("ProposeSessionMemoryCopy");
                await Click("AcceptSessionMemoryCopy");
                switch (fault)
                {
                    case "source-forget":
                        await composition.Memories.ExecuteAsync(new(MemoryCommandOperation.Forget,
                            f.Request.SessionId.Value, original.Id, original.Revision), RequestOrigin.LocalUi, () => true, f.Token);
                        break;
                    case "audit": checkpoint.Audit = (_, _) => throw new IOException("Required audit unavailable."); break;
                    case "commit": checkpoint.Commit = (_, _) => throw new IOException("Commit unconfirmed."); break;
                    case "control": checkpoint.Commit = (_, _) => composition.Access.ControlRevision++; break;
                    case "privacy": checkpoint.Commit = (_, _) => composition.Access.CanInspect = false; break;
                }
                var admission = window.SessionMemoryCopyOperation;
                if (string.Equals(fault, "cancel", StringComparison.Ordinal))
                {
                    window.FindControl<Button>("CancelSessionMemoryCopy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.SessionMemoryCopyOperation.Should().BeSameAs(admission, "cancellation must not replace the owned operation handle");
                    model.ConversationStatus.Should().Contain("review cancelled").And.Contain("No admission, rollback or retry");
                }
                else { await Click("AdmitSessionMemoryCopy"); }
                var outcome = window.FindControl<TextBox>("SessionMemoryCopyStatus")!;
                if (fault is "control" or "privacy")
                {
                    outcome.IsFocused.Should().BeFalse("revoked private control cannot receive a late focus handoff");
                }
                else
                {
                    outcome.IsFocused.Should().BeTrue(model.ConversationStatus);
                    outcome.Text.Should().Be(model.ConversationStatus).And.NotContain("admitted independently");
                    outcome.CaretIndex.Should().Be(0);
                }
                model.SessionMemoryCopyDescription.Should().BeEmpty();
                model.CanAdmitSessionMemoryCopy.Should().BeFalse();
                f.Count("reviewed_memory").Should().Be(1);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                checkpoint.Audit = null;
                checkpoint.Commit = null;
                composition.Access.CanInspect = true;
                model.DeviceProfileMemory = true;
                await model.ListMemoriesAsync();
                model.MemoryRecords.Should().BeEmpty("no failed or cancelled copy can create a durable profile source");
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task NativeSessionMemoryCopyPresentationCaptureCannotFollowSelectionScopeOrControlChanges()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composition = new Composition(f, memoryContext: true);
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        var current = model.CaptureSessionMemoryCopyPresentation();
        current().Should().BeTrue();
        model.SelectMemory(null);
        current().Should().BeFalse();
        current = model.CaptureSessionMemoryCopyPresentation();
        model.DeviceProfileMemory = true;
        current().Should().BeFalse();
        model.DeviceProfileMemory = false;
        current().Should().BeFalse("returning to the scope cannot renew the old presentation");
        current = model.CaptureSessionMemoryCopyPresentation();
        composition.Access.ControlRevision++;
        current().Should().BeFalse();
        current = model.CaptureSessionMemoryCopyPresentation();
        model.Close();
        current().Should().BeFalse();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("selection")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("source-forget")]
    [InlineData("scope")]
    [InlineData("close")]
    public async Task NativeSessionMemoryCopyRetiresWithoutProfileAdmissionAtPrivateAndSourceBoundaries(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composition = new Composition(f, memoryContext: true);
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.MemoryDraft = "Prefer explicit examples";
        await model.ProposeMemoryAsync();
        model.SelectMemory(model.MemoryRecords.Single());
        await model.InspectMemoryAsync();
        await model.ReviewMemoryAsync(true);
        await model.AdmitMemoryAsync();
        await model.InspectMemoryAsync();
        var original = model.SelectedMemory!;
        await model.KeepSessionMemoryAsync();
        model.SessionMemoryCopyClass = MemoryContentClass.Decision;
        await model.ProposeSessionMemoryCopyAsync();
        await model.AcceptSessionMemoryCopyAsync();
        model.CanAdmitSessionMemoryCopy.Should().BeTrue(model.ConversationStatus);
        switch (fault)
        {
            case "cancel": model.CancelSessionMemoryCopy(); break;
            case "selection": model.SelectMemory(null); break;
            case "privacy": composition.Access.CanInspect = false; break;
            case "control": composition.Access.ControlRevision++; break;
            case "source-forget":
                await composition.Memories.ExecuteAsync(new(MemoryCommandOperation.Forget,
                    f.Request.SessionId.Value, original.Id, original.Revision), RequestOrigin.LocalUi, () => true, f.Token);
                break;
            case "scope": model.DeviceProfileMemory = true; break;
            case "close": model.Close(); break;
        }
        await model.AdmitSessionMemoryCopyAsync();
        f.Count("reviewed_memory").Should().Be(1);
        model.CanAdmitSessionMemoryCopy.Should().BeFalse();
        model.SessionMemoryCopyDescription.Should().BeEmpty();
        f.Count("conversation_messages").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
        model.Close();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task NativeSessionMemoryCopyCannotRelabelInvocationHostOrVoiceInput(RequestOrigin origin)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composition = new Composition(f, memoryContext: true);
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.MemoryDraft = "Prefer explicit examples";
        await model.ProposeMemoryAsync();
        model.SelectMemory(model.MemoryRecords.Single());
        await model.InspectMemoryAsync();
        await model.ReviewMemoryAsync(true);
        await model.AdmitMemoryAsync();
        await model.InspectMemoryAsync();
        var input = InteractionStorageFixture.NewRequest(f.Request.SessionId, origin);
        var request = origin == RequestOrigin.LocalUi
            ? new HostRequest(input.RequestId, input.SessionId, input.TaskId, origin, new(Guid.NewGuid())) : input;
        using (HostActivity.BeginRoot(request,
            HostActivityLayer.Application, HostOperation.Request))
        { await model.KeepSessionMemoryAsync(); }
        model.ConversationStatus.Should().Contain("original native user authority");
        model.SessionMemoryCopyDescription.Should().BeEmpty();
        f.Count("reviewed_memory").Should().Be(1);
        model.Close();
    }
}
