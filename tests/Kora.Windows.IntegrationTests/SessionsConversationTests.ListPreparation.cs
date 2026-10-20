using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task UnconfirmedNativeSaveMakesListPreparationUnavailableWithoutDiscardOrRetry()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        model.ListSearchKind = SessionListSearchKind.Browse;
        await model.SearchListAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "uncertain original buffer";
        model.ReportComposerCloseFailure(new IOException("Unconfirmed native Save receipt"));
        model.DecideComposerDraft = () => throw new InvalidOperationException("Reconcile first; no automatic decision");
        (await model.PrepareListQueryAsync(new(Guid.NewGuid()), () => true, () => true)).Should().BeFalse();
        model.ComposerBody.Should().Be("uncertain original buffer");
        model.ListQuery.Should().BeEmpty();
        model.Status.Should().Contain("Reconcile");
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Revision.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StaleListPreparationDecisionCannotDiscardSaveOrRetargetTheOriginalSharedBuffer(int choice)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        model.ListSearchKind = SessionListSearchKind.Browse;
        await model.SearchListAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "original buffer before revoked native decision";
        var selected = model.SelectedSessionRecord;
        var admitted = true;
        model.DecideComposerDraft = () =>
        {
            admitted = false;
            return Task.FromResult((ComposerDraftDecision)choice);
        };
        (await model.PrepareListQueryAsync(new(Guid.NewGuid()), () => admitted, () => true)).Should().BeFalse();
        model.ComposerBody.Should().Be("original buffer before revoked native decision");
        model.SelectedSessionRecord.Should().Be(selected);
        model.ListQuery.Should().BeEmpty();
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Revision.Should().Be(0);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NativeListPreparationUsesTheActualDirtyGuardAndNativeSaveRequiresAFreshCommand(int decision)
    {
        var choice = (ComposerDraftDecision)decision;
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                model.ListSearchKind = SessionListSearchKind.Browse;
                await model.SearchListAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                var original = model.SelectedSessionRecord;
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                model.ComposerBody = "  exact shared e\u0301 private buffer\r\nnot a query  ";
                var body = model.ComposerBody;
                Window? dialog = null;
                using var observer = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
                {
                    if (string.Equals(view.Title, "Unsaved exact-session conversation draft", StringComparison.Ordinal)) { dialog = view; }
                });
                var id = new HostId<SessionIdentity>(Guid.NewGuid());
                using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
                    HostActivityLayer.Application, HostOperation.Request);
                var preparation = model.PrepareListQueryAsync(id, () => true, window.FocusPreparedListQuery);
                dialog.Should().NotBeNull();
                dialog!.Owner.Should().BeSameAs(window);
                model.ComposerBody.Should().Be(body);
                dialog.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "Draft" + choice, StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await preparation).Should().Be(choice == ComposerDraftDecision.Discard);
                if (choice == ComposerDraftDecision.Discard)
                {
                    model.ComposerBody.Should().BeEmpty();
                    model.SelectedSessionRecord.Should().BeNull();
                    model.ListQuery.Should().Be(id.Value.ToString("D"));
                    model.Sessions.Should().BeEmpty();
                }
                else
                {
                    model.ComposerBody.Should().Be(body);
                    model.SelectedSessionRecord.Should().Be(original);
                    model.ListQuery.Should().BeEmpty();
                    model.Status.Should().Contain("cancelled");
                }
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should()
                    .Be(choice == ComposerDraftDecision.Save ? body : null);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                f.Count("conversation_messages").Should().Be(0);
                if (choice == ComposerDraftDecision.Save)
                {
                    model.ComposerDirty.Should().BeFalse();
                    (await model.PrepareListQueryAsync(id, () => true, window.FocusPreparedListQuery)).Should().BeTrue();
                }
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListPreparationPreservesOrdinarySelectionSharedComposerAndNameDraftOrRefusesSearchSelectionRetirement(bool searched)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        if (searched) { model.ListSearchKind = SessionListSearchKind.Browse; await model.SearchListAsync(); }
        else { await model.RefreshAsync(); }
        await model.SelectAsync(model.Sessions.Single());
        var original = model.SelectedSessionRecord;
        model.NameDraft = "unsaved exact name editor";
        model.ComposerBody = "nonempty shared composer";
        model.DecideComposerDraft = () => throw new InvalidOperationException("Name editor must not be dropped");
        var draft = await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token);
        (await model.PrepareListQueryAsync(new(Guid.NewGuid()), () => true, () => true)).Should().Be(!searched);
        model.NameDraft.Should().Be("unsaved exact name editor");
        model.ComposerBody.Should().Be("nonempty shared composer");
        model.SelectedSessionRecord.Should().Be(original);
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Should().Be(draft);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task ExistingNativeComposerReviewIsNotReplacedAndFocusFailureNeverAcknowledgesPreparation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        model.ListSearchKind = SessionListSearchKind.Browse;
        await model.SearchListAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        using var comparison = model.CompareComposer();
        comparison.Should().NotBeNull();
        (await model.PrepareListQueryAsync(new(Guid.NewGuid()), () => true, () => true)).Should().BeFalse();
        comparison!.Available.Should().BeTrue();
        model.CloseComposerComparison(comparison);
        (await model.PrepareListQueryAsync(new(Guid.NewGuid()), () => true, () => false)).Should().BeFalse();
        model.Status.Should().Contain("focus").And.NotContain("PREPARED");
    }
}
