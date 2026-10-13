using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.Controls;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("selection")]
    [InlineData("source")]
    public async Task CatalogueLateNavigationCannotSwitchAfterOriginalPrivateSourceOrSelectionRevocation(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "private target draft");
        var checkpoint = new CatalogueCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        var other = model.Sessions.Single(row => row.Authority.SessionId != original);
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var gate = true;
        using var viewer = HostActivity.BeginRoot(WindowsSqliteConversationTests.Fresh(original), HostActivityLayer.Desktop, HostOperation.Request);
        var exact = (await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token)).Records.Single();
        checkpoint.Read = () =>
        {
            checkpoint.Read = null;
            switch (boundary)
            {
                case "private": composed.Access.CanInspect = false; break;
                case "control": composed.Access.ControlRevision++; break;
                case "cancel": cancellation.Cancel(); break;
                case "close": model.Close(); break;
                case "selection": _ = model.SelectAsync(other); break;
                default: gate = false; break;
            }
        };
        var action = () => model.SelectSavedDraftAsync(exact, () => gate, cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        model.ComposerBody.Should().BeEmpty();
        model.ConversationMessages.Should().BeEmpty();
        if (boundary is not "close") { model.SelectedSessionRecord!.Authority.SessionId.Should().Be(original); }
        f.Count("conversation_messages").Should().Be(0);
    }

    private sealed class CatalogueCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal Action? Read { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeConversationDraftMetadataRead(SqliteCommand command) => Read?.Invoke();
    }

    [Fact]
    public async Task ActualNativeCatalogueRefreshSelectionNavigationAndReloadAreSeparateExplicitActions()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var saved = await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "PRIVATE_CATALOGUE_BODY_43562");
            f.Request = WindowsSqliteConversationTests.Fresh(saved.Session.SessionId);
            await f.RunAsync(async () =>
            {
                await f.Store.RecordControlIntentAsync(f.Request, f.Token);
                await f.Store.ChangeIdleLifecycleAsync(f.Request, new(1), false, () => true, f.Token);
            });
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, composed.Access);
            var model = new SavedDraftCatalogueViewModel(workspace, composed.Model.SelectSavedDraftAsync,
                composed.Access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var catalogue = new SavedDraftCatalogueWindow(model);
            var sessions = new SessionsWindow(composed.Model);
            catalogue.Show();
            sessions.Show();
            try
            {
                model.Records.Should().BeEmpty();
                catalogue.FindControl<Button>("RefreshPage")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await catalogue.Operation;
                catalogue.UpdateLayout();
                var exact = model.Records.Single();
                var list = catalogue.GetVisualDescendants().OfType<ScrollableListBox>().Single();
                list.SelectedItem = exact;
                ControlAutomationPeer.CreatePeerForElement(list)!.GetName().Should().Contain("no body preview");
                model.SelectedDetails.Should().Contain(saved.Session.SessionId.Value.ToString("D")).And.NotContain("PRIVATE_CATALOGUE_BODY");
                catalogue.FindControl<Button>("OpenExact")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await catalogue.Operation;
                composed.Model.SelectedSessionRecord!.Authority.Should().Be(exact.Session);
                composed.Model.ComposerBody.Should().BeEmpty();
                composed.Model.ConversationMessages.Should().BeEmpty();
                composed.Model.CanReloadConversationDraft.Should().BeTrue();
                composed.Model.CanCompose.Should().BeFalse("retained Done navigation is historical, not Resume");
                composed.Model.CanSubmitConversation.Should().BeFalse();
                composed.Model.ComposerTarget.Should().Contain("saved draft revision 1");
                sessions.FindControl<Button>("ReloadConversationDraft")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await sessions.ConversationOperation;
                composed.Model.ComposerBody.Should().Be(saved.Draft.Body);
                composed.Model.CanSubmitConversation.Should().BeFalse();
                f.Count("conversation_messages").Should().Be(0);
                model.Revoke();
                model.SelectedDetails.Should().BeEmpty();
                model.Records.Should().BeEmpty();
                catalogue.FindControl<Button>("CloseView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Status.Should().BeEmpty();
            }
            finally { catalogue.Close(); sessions.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CatalogueNavigationProtectsOriginalDirtyBufferAndNeverOverwritesTarget(int choice)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var saved = await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "target saved body");
        await using var composed = new Composition(f);
        await composed.Model.RefreshAsync();
        await composed.Model.SelectAsync(composed.Model.Sessions.Single(row => row.Authority.SessionId == original));
        await composed.Model.ReadConversationAsync();
        composed.Model.ComposerBody = "original dirty body";
        composed.Model.DecideComposerDraft = () => Task.FromResult((ComposerDraftDecision)choice);
        using var viewer = HostActivity.BeginRoot(WindowsSqliteConversationTests.Fresh(original), HostActivityLayer.Desktop, HostOperation.Request);
        var exact = (await f.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, f.Token)).Records.Single();
        if ((ComposerDraftDecision)choice == ComposerDraftDecision.Save)
        {
            var changed = () => composed.Model.SelectSavedDraftAsync(exact, () => true, f.Token);
            await changed.Should().ThrowAsync<InvalidOperationException>("the explicit dirty-buffer save changes the conservative authority head; Refresh is required");
        }
        else
        {
            var opened = await composed.Model.SelectSavedDraftAsync(exact, () => true, f.Token);
            opened.Should().Be((ComposerDraftDecision)choice == ComposerDraftDecision.Discard);
        }
        var cancel = (ComposerDraftDecision)choice == ComposerDraftDecision.Cancel;
        composed.Model.SelectedSessionRecord!.Authority.SessionId.Should().Be(
            (ComposerDraftDecision)choice == ComposerDraftDecision.Discard ? saved.Session.SessionId : original);
        composed.Model.ComposerBody.Should().Be(cancel || (ComposerDraftDecision)choice == ComposerDraftDecision.Save
            ? "original dirty body" : string.Empty);
        (await f.Store.ReadConversationDraftAsync(saved.Session.SessionId, f.Token)).Should().Be(saved);
        (await f.Store.ReadConversationDraftAsync(original, f.Token)).Draft.Body.Should().Be(
            (ComposerDraftDecision)choice == ComposerDraftDecision.Save ? "original dirty body" : null);
        f.Count("conversation_messages").Should().Be(0);
    }
}
