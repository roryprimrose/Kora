using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AllWorkWindowTests
{
    [WindowsFact]
    public async Task LoadedPageOrderIsAccessibleCachedNoReadAndRetiresExactRowsWithoutChangingRawNext()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var owner = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            for (var index = 0; index < 12; index++)
            {
                await owner.CreateAsync(new(index % 2 == 0 ? "Zebra" : "Alpha"),
                    Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
            }
            var held = new GatedWorkStore(f.Store);
            var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Ordering cannot navigate."));
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var control = window.FindControl<ComboBox>("LoadedPageOrder")!;
                var peer = ControlAutomationPeer.CreatePeerForElement(control)!;
                peer.GetName().Should().Contain("this loaded page only").And.Contain("no read")
                    .And.Contain("Observed grants no navigation authority, selection or permission");
                peer.GetProvider<ISelectionProvider>()!.GetSelection().Should()
                    .ContainSingle().Which.GetName().Should().Be("Source (unchanged)");
                model.OrderChoices.Select(choice => choice.Label).Should().Equal("Source (unchanged)",
                    "Canonical session ID ascending", "Canonical session ID descending",
                    "Observed rows first (category only)", "Session name ascending (Ordinal; unnamed last)",
                    "Session name descending (Ordinal; unnamed last)");
                AllWorkViewModel.OrderScope.Should().Contain("no global ordering").And.Contain("not ready, allowed, succeeded")
                    .And.Contain("currentness").And.Contain("raw Next are unchanged");
                model.Order = SessionWorkOverviewPageOrder.SessionNameAscending;
                model.ClearOrder();
                held.Reads.Should().Be(0);
                held.MetadataReads.Should().Be(0);
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_tasks");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                await model.RefreshAsync();
                var source = model.Records.ToArray();
                source.Should().HaveCount(10);
                var rawAfter = source[^1].Session.Authority.SessionId.Value;
                model.CanNext.Should().BeTrue();
                model.Select(source[0]);
                model.CanNavigate.Should().BeTrue();
                var reads = held.Reads;
                var metadataReads = held.MetadataReads;
                var reentrant = 0;
                System.ComponentModel.PropertyChangedEventHandler onOrder = (_, args) =>
                {
                    if (args.PropertyName is not nameof(AllWorkViewModel.Order)) { return; }
                    reentrant++;
                    model.Select(model.Records[0]);
                    model.Order = SessionWorkOverviewPageOrder.ObservedFirst;
                };
                model.PropertyChanged += onOrder;
                control.Focus().Should().BeTrue();
                control.SelectedIndex = 2;
                model.Order.Should().Be(SessionWorkOverviewPageOrder.SessionIdDescending);
                reentrant.Should().Be(1);
                model.PropertyChanged -= onOrder;
                control.IsFocused.Should().BeTrue();
                model.Records.Should().Equal(source.Reverse());
                model.SelectedRecord.Should().BeNull();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.Select(source[0]);
                model.SelectedRecord.Should().BeNull("callbacks from an older projection are retired");
                foreach (var order in Enum.GetValues<SessionWorkOverviewPageOrder>())
                {
                    model.Order = order;
                    var cached = model.Records;
                    model.Records.Should().BeSameAs(cached);
                    model.Records.Should().BeEquivalentTo(source);
                    model.CanNext.Should().BeTrue();
                }
                held.Reads.Should().Be(reads);
                held.MetadataReads.Should().Be(metadataReads);
                window.FindControl<Button>("ClearLoadedPageOrder")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Order.Should().Be(SessionWorkOverviewPageOrder.Source);
                model.Records.Should().Equal(source);
                await model.RefreshAsync(next: true);
                held.LastMetadataAfter.Should().Be(rawAfter, "Next uses the raw admitted continuation, not the visible last row");
                model.Records.Should().HaveCount(3);
                model.CanNext.Should().BeFalse();
                model.Select(model.Records[0]);
                model.ClearOrder();
                model.SelectedRecord.Should().BeNull("Clear retires selection even when Source was already selected");
                var invalidOrder = () => model.Order = (SessionWorkOverviewPageOrder)99;
                invalidOrder.Should().Throw<ArgumentOutOfRangeException>();
                held.Writes.Should().Be(0);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audits);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                window.Close();
                model.Order.Should().Be(SessionWorkOverviewPageOrder.Source);
                model.Records.Should().BeEmpty();
                control.IsEnabled.Should().BeFalse();
                model.Order = SessionWorkOverviewPageOrder.SessionIdDescending;
                model.Order.Should().Be(SessionWorkOverviewPageOrder.Source);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("order")]
    [InlineData("clear")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("close")]
    public async Task OrderRetirementCancelsLateExactNavigationAndPreservesOnlyStillAdmittedRawPage(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new GatedWorkStore(f.Store);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = true;
        var cancelled = false;
        var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
            async (_, admitted, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(f.Token);
                accepted = admitted();
                cancelled = token.IsCancellationRequested;
            });
        model.TaskIdText = f.Request.TaskId.Value.ToString("D");
        model.RequestIdText = f.Request.RequestId.Value.ToString("D");
        model.TaskState = (await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 50, f.Token)).Records.Single().State;
        await model.RefreshAsync();
        model.Order = SessionWorkOverviewPageOrder.SessionIdAscending;
        var stale = model.Records.Single();
        model.Select(stale);
        var readCount = held.Reads;
        var metadataCount = held.MetadataReads;
        var navigation = model.NavigateAsync();
        await entered.Task.WaitAsync(f.Token);
        if (boundary is "clear") { model.ClearOrder(); }
        else { model.Order = SessionWorkOverviewPageOrder.SessionNameDescending; }
        if (boundary is "privacy") { access.CanInspect = false; }
        if (boundary is "control") { access.ControlRevision++; }
        if (boundary is "source") { model.Revoke(); }
        if (boundary is "close") { model.Close(); }
        release.SetResult();
        await navigation;
        accepted.Should().BeFalse();
        cancelled.Should().BeTrue();
        held.Reads.Should().Be(readCount);
        held.MetadataReads.Should().Be(metadataCount);
        model.SelectedRecord.Should().BeNull();
        model.SelectedDetails.Should().BeEmpty();
        model.CanNavigate.Should().BeFalse();
        model.Select(stale);
        model.SelectedRecord.Should().BeNull();
        if (boundary is "order" or "clear")
        {
            model.Records.Should().ContainSingle().Which.Should().Be(stale);
            model.TaskIdText.Should().Be(f.Request.TaskId.Value.ToString("D"));
            model.RequestIdText.Should().Be(f.Request.RequestId.Value.ToString("D"));
            model.TaskState.Should().NotBeNull();
            model.Select(model.Records.Single());
            model.CanNavigate.Should().BeTrue("only a new explicit exact-row selection can restore readiness");
        }
        else { model.Records.Should().BeEmpty(); }
        model.Close();
        model.Records.Should().BeEmpty();
        held.Writes.Should().Be(0);
    }
}
