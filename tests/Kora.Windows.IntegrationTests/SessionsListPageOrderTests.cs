using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsListPageOrderTests
{
    [Fact]
    public async Task AccessibleNativeOrderAndClearPreserveExactSelectionDirtyBuffersQuestionsAndReviewFences()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            await service.RenameAsync(f.Request.SessionId, new(1), 0, new("middle"), RequestOrigin.LocalUi, f.Token);
            await service.CreateAsync(new("A"), RequestOrigin.LocalUi, f.Token);
            await service.CreateAsync(new("z"), RequestOrigin.LocalUi, f.Token);
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance);
            SessionsWindow? window = null;
            try
            {
                // Construct the native owner only after setup; Attach starts its refresh timer.
                model.ListSearchKind = SessionListSearchKind.Browse;
                await model.SearchListAsync();
                var source = model.Sessions.ToArray();
                var target = source.Single(row => row.Authority.SessionId == f.Request.SessionId);
                await model.SelectAsync(target);
                model.SelectedSessionRecord.Should().Be(target);
                var exactSelection = model.SelectedSessionRecord;
                model.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                var pending = model.PendingQuestions.Single();
                var work = model.WorkRecords.ToArray();
                var live = model.CaptureNavigationEligibility();
                model.ComposerBody = "unsaved exact composer";
                model.NameDraft = "unsaved name";
                var decisions = 0;
                window = new SessionsWindow(model);
                model.DecideComposerDraft = () => { decisions++; return Task.FromResult(ComposerDraftDecision.Cancel); };
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_tasks");
                window.Show();
                var combo = window.FindControl<ComboBox>("ListPageOrder")!;
                var clear = window.FindControl<Button>("ClearListPageOrder")!;
                combo.ItemsSource.Should().BeEquivalentTo(Enum.GetValues<SessionListPageOrder>());
                combo.Focus().Should().BeTrue();
                combo.IsEnabled.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(combo)!.GetName().Should().Contain("loaded session page only")
                    .And.Contain("Source").And.Contain("GenerationDescending").And.Contain("never global");
                ControlAutomationPeer.CreatePeerForElement(clear)!.GetName().Should().Contain("without reading or changing selection");
                model.ListPageOrderScope.Should().Contain("not the ordinary Refresh list").And.Contain("unnamed last")
                    .And.Contain("canonical session ID").And.Contain("Next keeps the original scan");
                var notified = false;
                void Reenter(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (!string.Equals(args.PropertyName, nameof(model.Sessions), StringComparison.Ordinal)) { return; }
                    notified = true;
                    model.ListPageOrder = SessionListPageOrder.GenerationAscending;
                    _ = model.SelectAsync(model.Sessions[0]);
                }
                model.PropertyChanged += Reenter;
                try
                {
                    foreach (var order in Enum.GetValues<SessionListPageOrder>())
                    {
                        combo.SelectedItem = order;
                        model.ListPageOrder.Should().Be(order, "binding and reentrant callbacks must not replace the requested order");
                        model.Sessions.Should().Equal(SessionListPageOrdering.Apply([.. source], order));
                        model.SelectedSessionRecord.Should().BeSameAs(exactSelection);
                        window.FindControl<ListBox>("Records")!.SelectedItem.Should().Be(target);
                        model.ComposerBody.Should().Be("unsaved exact composer");
                        model.ComposerDirty.Should().BeTrue();
                        model.NameDraft.Should().Be("unsaved name");
                        model.PendingQuestions.Should().ContainSingle().Which.Should().BeSameAs(pending);
                        model.WorkRecords.Should().Equal(work);
                        live().Should().BeTrue("presentation order does not renew or retire selection authority");
                    }
                }
                finally { model.PropertyChanged -= Reenter; }
                notified.Should().BeTrue();
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.ListPageOrder.Should().Be(SessionListPageOrder.Source);
                model.Sessions.Should().Equal(source);
                model.SelectedSessionRecord.Should().BeSameAs(exactSelection);
                model.ComposerBody.Should().Be("unsaved exact composer");
                model.NameDraft.Should().Be("unsaved name");
                decisions.Should().Be(0, "ordering is not navigation and must not save, discard or prompt");
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
                model.RevokeSessionList();
                model.Sessions.Should().BeEmpty();
                model.SelectedSessionRecord.Should().BeNull();
                live().Should().BeFalse();
                combo.IsEnabled.Should().BeFalse();
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Sessions.Should().BeEmpty();
            }
            finally { model.Close(); window?.Close(); }
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("close")]
    public async Task PendingDirtyNavigationCannotSelectAFormerRowAfterSourceOrAdmissionChanges(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        await service.CreateAsync(new("other"), RequestOrigin.LocalUi, f.Token);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            model.ListSearchKind = SessionListSearchKind.Browse;
            await model.SearchListAsync();
            await model.SelectAsync(model.Sessions[0]);
            var original = model.SelectedSessionRecord;
            var candidate = model.Sessions[1];
            model.ComposerBody = "dirty";
            var decision = new TaskCompletionSource<ComposerDraftDecision>();
            model.DecideComposerDraft = () => decision.Task;
            var navigation = model.SelectAsync(candidate);
            model.CanOrderListPage.Should().BeFalse();
            model.ListPageOrder = SessionListPageOrder.NameDescending;
            model.ListPageOrder.Should().Be(SessionListPageOrder.Source);
            if (change is "source") { model.RevokeSessionList(); }
            if (change is "privacy") { access.CanInspect = false; }
            if (change is "control") { access.ControlRevision++; }
            if (change is "close") { model.Close(); }
            decision.SetResult(ComposerDraftDecision.Discard);
            await navigation;
            model.SelectedSessionRecord.Should().NotBeSameAs(candidate);
            if (change is "control") { model.SelectedSessionRecord.Should().BeSameAs(original); }
            else { model.SelectedSessionRecord.Should().BeNull(); }
            model.ListPageOrder.Should().Be(SessionListPageOrder.Source);
        }
        finally { model.Close(); }
    }
}
