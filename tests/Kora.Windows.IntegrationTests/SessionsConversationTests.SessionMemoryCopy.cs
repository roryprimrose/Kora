using Avalonia.Controls;
using Avalonia.Interactivity;
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
                    async Task Click(string name)
                    {
                        var button = window.FindControl<Button>(name)!;
                        button.IsEnabled.Should().BeTrue(model.ConversationStatus);
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await window.SessionMemoryCopyOperation;
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
