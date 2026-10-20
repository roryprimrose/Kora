using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Storage;
using Kora.Controls;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeLoadedPageOrderIsAccessibleReadFreeAndKeepsOriginalNextAndComposer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            for (var index = 0; index <= SavedDraftPage.MaximumRecords; index++)
            {
                if (index != 0)
                {
                    f.Request = InteractionStorageFixture.NewRequest();
                    await f.AdmitAsync(newSession: true);
                    await WindowsSqliteConversationTests.FinishInitial(f);
                }
                await WindowsSqliteSessionWorkspaceTests.Service(f, new()).RenameAsync(f.Request.SessionId, new(1), 0,
                    new(f.Request.SessionId.Value.ToString("D")), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                await WindowsSqliteConversationTests.Save(f,
                    await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "private saved body");
            }
            var reads = 0;
            var checkpoint = new CatalogueCheckpoint { Read = () => reads++ };
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            await using var composed = new Composition(f);
            await composed.Model.RefreshAsync();
            await composed.Model.SelectAsync(composed.Model.Sessions[0]);
            await composed.Model.ReadConversationAsync();
            composed.Model.ComposerBody = "unchanged dirty composer";
            var target = composed.Model.SelectedSessionRecord;
            var decisions = 0;
            composed.Model.DecideComposerDraft = () =>
            {
                decisions++;
                return Task.FromResult(ComposerDraftDecision.Cancel);
            };
            using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, composed.Access),
                composed.Model.SelectSavedDraftAsync, composed.Access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var window = new SavedDraftCatalogueWindow(model);
            window.Show();
            try
            {
                var order = window.FindControl<ComboBox>("DraftPageOrder")!;
                var list = window.FindControl<ScrollableListBox>("DraftRecords")!;
                order.Focusable.Should().BeTrue();
                order.IsEnabled.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(order)!.GetName().Should()
                    .Contain("Order this loaded page").And.Contain("no read").And.Contain("not global");
                model.Orders.Select(option => option.Label).Should().Equal("Source order (default)",
                    "Session name ascending", "Session name descending", "Saved revision ascending", "Saved revision descending");
                model.Order.Should().Be(SavedDraftPageOrder.Source);
                model.Records.Should().BeEmpty();
                reads.Should().Be(0);
                model.RevisionText = "1";
                model.MinimumSessionGenerationText = "1";
                model.MaximumSessionGenerationText = "1";
                model.MinimumControlRevisionText = "0";
                model.MinimumBytesText = "1";
                model.MaximumScalarsText = "4096";
                await model.Refresh();
                var source = model.Records.ToArray();
                source.Should().HaveCount(SavedDraftPage.MaximumRecords);
                var originalReads = reads;
                var audits = f.Count("security_audit_events");
                foreach (var choice in model.Orders.Skip(1).Concat(model.Orders.Take(1)))
                {
                    list.SelectedItem = model.Records[0];
                    model.CanNavigate.Should().BeTrue();
                    order.SelectedItem = choice;
                    model.Order.Should().Be(choice.Value);
                    model.SelectedRecord.Should().BeNull();
                    list.SelectedItem.Should().BeNull();
                    model.SelectedDetails.Should().BeEmpty();
                    window.FindControl<Button>("OpenExact")!.IsEnabled.Should().BeFalse();
                    model.CanNext.Should().BeTrue();
                    model.RevisionText.Should().Be("1");
                    model.MinimumSessionGenerationText.Should().Be("1");
                    model.MaximumSessionGenerationText.Should().Be("1");
                    model.MinimumControlRevisionText.Should().Be("0");
                    model.MinimumBytesText.Should().Be("1");
                    model.MaximumScalarsText.Should().Be("4096");
                    reads.Should().Be(originalReads);
                    f.Count("security_audit_events").Should().Be(audits);
                    composed.Model.ComposerBody.Should().Be("unchanged dirty composer");
                    composed.Model.SelectedSessionRecord.Should().BeSameAs(target);
                    decisions.Should().Be(0);
                }
                model.Records.Should().Equal(source);
                order.SelectedItem = model.Orders.Single(choice => choice.Value == SavedDraftPageOrder.SessionNameDescending);
                model.Records.Should().Equal(source.Reverse());
                window.UpdateLayout();
                order.IsEffectivelyVisible.Should().BeTrue();
                list.Bounds.Height.Should().BeGreaterThan(0);
                model.OrderHint.Should().Contain("unnamed last").And.Contain("original source continuation").And.Contain("no global");
                window.FindControl<Button>("NextPage")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                var next = model.Records.Single();
                string.CompareOrdinal(next.Session.SessionId.Value.ToString("D"),
                    source[^1].Session.SessionId.Value.ToString("D")).Should().BeGreaterThan(0);
                model.CanNext.Should().BeFalse();
                model.Order.Should().Be(SavedDraftPageOrder.SessionNameDescending);
                model.Select(next);
                var beforeClear = reads;
                window.FindControl<Button>("ClearQuery")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Order.Should().Be(SavedDraftPageOrder.Source);
                order.SelectedItem.Should().Be(model.Orders[0]);
                reads.Should().Be(beforeClear);
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNext.Should().BeFalse();
                decisions.Should().Be(0);
                await model.Refresh();
                model.Select(model.Records[0]);
                var revokedDuringNotification = false;
                model.PropertyChanged += (_, args) =>
                {
                    if (!revokedDuringNotification && string.Equals(args.PropertyName, nameof(model.Status), StringComparison.Ordinal))
                    {
                        revokedDuringNotification = true;
                        model.Revoke();
                    }
                };
                var beforeRevoke = reads;
                order.SelectedItem = model.Orders.Single(choice => choice.Value == SavedDraftPageOrder.SavedRevisionAscending);
                revokedDuringNotification.Should().BeTrue();
                reads.Should().Be(beforeRevoke);
                model.Order.Should().Be(SavedDraftPageOrder.Source);
                model.Records.Should().BeEmpty();
                list.Items.Should().BeEmpty("source revocation must notify even inside an ordering notification");
                model.SelectedDetails.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("admission")]
    [InlineData("cancel")]
    [InlineData("close")]
    public async Task SortedLoadedPageLosesAllAuthorityOnRevocation(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "private saved body");
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var admitted = true;
        using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            (_, _, _) => Task.FromResult(false), access, () => admitted, NullLogger<SavedDraftCatalogueViewModel>.Instance);
        await model.Refresh();
        model.Order = SavedDraftPageOrder.SavedRevisionDescending;
        model.Select(model.Records.Single());
        switch (boundary)
        {
            case "source": model.Revoke(); break;
            case "control": access.ControlRevision++; model.SynchronizeAuthority(); break;
            case "private": access.CanInspect = false; model.SynchronizeAuthority(); break;
            case "admission": admitted = false; model.SynchronizeAuthority(); break;
            case "cancel": model.Cancel(); break;
            default: model.Dispose(); break;
        }
        model.Records.Should().BeEmpty();
        model.SelectedRecord.Should().BeNull();
        model.SelectedDetails.Should().BeEmpty();
        model.Order.Should().Be(SavedDraftPageOrder.Source);
        model.CanNext.Should().BeFalse();
        model.CanNavigate.Should().BeFalse();
    }

    [Fact]
    public async Task ReorderingRetiresLateNavigationCallbackWithoutLosingTheAdmittedPage()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "private body");
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<bool>? oldAuthority = null;
        CancellationToken oldToken = default;
        using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            async (_, current, token) =>
            {
                oldAuthority = current;
                oldToken = token;
                arrived.SetResult();
                await release.Task;
                return true;
            }, access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
        await model.Refresh();
        var row = model.Records.Single();
        model.Select(row);
        var navigation = model.Navigate();
        await arrived.Task;
        model.Order = SavedDraftPageOrder.SavedRevisionAscending;
        oldAuthority!().Should().BeFalse();
        oldToken.IsCancellationRequested.Should().BeTrue();
        model.SelectedDetails.Should().BeEmpty();
        release.SetResult();
        await navigation;
        model.Records.Should().Equal(row);
        model.SelectedRecord.Should().BeNull();
        model.CanNavigate.Should().BeFalse();
        model.Status.Should().Contain("Order this loaded page changed").And.NotContain("Opened");
        model.Select(row);
        model.CanNavigate.Should().BeTrue();
        oldAuthority!().Should().BeFalse("reselection cannot resurrect the old navigation epoch");
    }
}
