using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoNativeViewsShareExactSelectionBufferAndAdmittedUserHostRowsWithoutPassiveImportOrActivity(bool workspaceFirst)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                var before = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                model.SelectedSessionRecord.Should().BeNull();
                model.ConversationMessages.Should().BeEmpty();
                model.CanSubmitConversation.Should().BeFalse();
                model.ComposerTarget.Should().Contain("Select an exact session");
                f.Count("conversation_drafts").Should().Be(0);
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                const string body = "  shared original e\u0301🙂\r\nsame target  ";
                workspace.FindControl<TextBox>("ConversationComposer")!.Text = body;
                compact.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(body);
                compact.FindControl<TextBox>("ConversationComposer")!.Text = body + " edited";
                workspace.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(body + " edited");
                await model.SelectAsync(model.SelectedSessionRecord);
                model.ComposerBody.Should().Be(body + " edited", "projecting the already-selected row must not clear a shared draft");
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
                f.Count("conversation_messages").Should().Be(0);
                if (workspaceFirst) { workspace.Close(); } else { compact.Close(); }
                model.ComposerBody.Should().Be(body + " edited");
                model.CanSubmitConversation.Should().BeTrue();
                var surviving = workspaceFirst ? (Window)compact : workspace;
                Click(surviving, "SubmitConversation");
                await (workspaceFirst ? compact.ConversationOperation : workspace.ConversationOperation);
                model.ConversationStatus.Should().Contain("HOST Unavailable").And.Contain("UnregisteredModel");
                model.ComposerBody.Should().BeEmpty();
                Click(surviving, "ReadConversation");
                await (workspaceFirst ? compact.ConversationOperation : workspace.ConversationOperation);
                model.ConversationMessages.Single().Body.Should().Be(body + " edited");
                model.ConversationMessages.Single().ExpectedTaskState.Should().Be(HostTaskState.Unavailable);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                surviving.Close();
                model.CanRead.Should().BeFalse();
                model.ConversationMessages.Should().BeEmpty();
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task LastNativeOwnerUsesOneCorrectlyOwnedSaveDiscardCancelDecision(bool compactLast, int choice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "original shared dirty body";
                Window? decision = null;
                var decisions = 0;
                using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
                {
                    if (!string.Equals(view.Title, "Unsaved exact-session conversation draft", StringComparison.Ordinal)) { return; }
                    decision = view;
                    decisions++;
                });
                var first = compactLast ? (Window)workspace : compact;
                var last = compactLast ? (Window)compact : workspace;
                first.Close();
                decisions.Should().Be(0);
                last.Close();
                last.Close();
                decisions.Should().Be(1);
                decision!.Owner.Should().BeSameAs(last);
                model.CanRead.Should().BeFalse();
                model.ComposerBody = "hostile second-surface edit while awaiting decision";
                model.ComposerBody.Should().Be("original shared dirty body");
                decision.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "Draft" + (ComposerDraftDecision)choice, StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.CloseOperation;
                var cancelled = (ComposerDraftDecision)choice == ComposerDraftDecision.Cancel;
                last.IsVisible.Should().Be(cancelled);
                if (cancelled)
                {
                    model.ComposerBody.Should().Be("original shared dirty body");
                    model.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
                }
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should()
                    .Be((ComposerDraftDecision)choice == ComposerDraftDecision.Save ? "original shared dirty body" : null);
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SharedNavigationFreezesOriginalDraftAndRejectsConcurrentEditsAndDecisions(int choice)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == original));
        await model.ReadConversationAsync();
        model.ComposerBody = "frozen original draft";
        var decision = new TaskCompletionSource<ComposerDraftDecision>();
        var decisions = 0;
        model.DecideComposerDraft = () => { decisions++; return decision.Task; };
        var next = model.Sessions.Single(entry => entry.Authority.SessionId != original);
        var switching = model.SelectAsync(next);
        model.ComposerBody = "must not overwrite the first surface";
        await model.SelectAsync(next);
        (await model.ProtectComposerAsync()).Should().BeFalse();
        decisions.Should().Be(1);
        model.ComposerBody.Should().Be("frozen original draft");
        decision.SetResult((ComposerDraftDecision)choice);
        await switching;
        if ((ComposerDraftDecision)choice == ComposerDraftDecision.Cancel)
        {
            model.SelectedSessionRecord!.Authority.SessionId.Should().Be(original);
            model.ComposerBody.Should().Be("frozen original draft");
        }
        else
        {
            model.SelectedSessionRecord!.Authority.SessionId.Should().Be(next.Authority.SessionId);
            model.ComposerBody.Should().BeEmpty();
        }
        (await f.Store.ReadConversationDraftAsync(original, f.Token)).Draft.Body.Should()
            .Be((ComposerDraftDecision)choice == ComposerDraftDecision.Save ? "frozen original draft" : null);
    }

    [Fact]
    public async Task FailedLastOwnerStaleSaveKeepsOriginalDraftAndTargetOpenWithoutFalseSuccessfulClose()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var compact = new CompactSessionWindow(model, owner);
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "original recoverable dirty draft";
                await composed.Conversation.SaveDraftAsync(await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token),
                    "independent newer saved revision", () => true, f.Token);
                model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Save);
                compact.Close();
                await owner.CloseOperation;
                compact.IsVisible.Should().BeTrue();
                model.ComposerDirty.Should().BeTrue();
                model.ComposerBody.Should().Be("original recoverable dirty draft");
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
                model.ConversationStatus.Should().Contain("unconfirmed");
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should()
                    .Be("independent newer saved revision");
            }
            finally { owner.Revoke(); }
        });
    }

    [Fact]
    public async Task PrivacyRevokesBothNativeViewsAndFencesDelayedDecisionWithoutSavingOrRetargeting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "private shared body";
            var decision = new TaskCompletionSource<ComposerDraftDecision>();
            model.DecideComposerDraft = () => decision.Task;
            var protect = model.ProtectComposerAsync();
            owner.Revoke();
            decision.SetResult(ComposerDraftDecision.Save);
            (await protect).Should().BeFalse();
            workspace.IsVisible.Should().BeFalse();
            compact.IsVisible.Should().BeFalse();
            workspace.FindControl<TextBox>("ConversationComposer")!.Text.Should().BeNullOrEmpty();
            compact.FindControl<TextBox>("ConversationComposer")!.Text.Should().BeNullOrEmpty();
            model.SelectedSessionRecord.Should().BeNull();
            model.ComposerBody.Should().BeEmpty();
            model.CanCompose.Should().BeFalse();
            f.Count("conversation_drafts").Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedPhysicalCommitFencesControlLossAndOneShotConcurrentSubmission(bool revokeControl)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            Composition? subject = null;
            f.Reopen(new SelectionCheckpoint(() =>
            {
                if (revokeControl) { subject!.Access.ControlRevision++; }
                subject!.Model.SubmitConversationAsync().GetAwaiter().GetResult();
                subject.Model.ComposerBody = "not admitted second buffer";
            }));
            await f.Store.InitializeAsync(f.Token);
            await using var composed = new Composition(f);
            subject = composed;
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "single original submission";
                Click(compact, "SubmitConversation");
                await compact.ConversationOperation;
                if (revokeControl)
                {
                    f.Count("conversation_messages").Should().Be(0);
                    model.ComposerBody.Should().BeEmpty("private control epoch changed; both views revoke private content");
                    model.CanSubmitConversation.Should().BeFalse();
                }
                else
                {
                    f.Count("conversation_messages").Should().Be(1);
                    model.ComposerBody.Should().BeEmpty();
                    (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token))
                        .Messages.Single().Body.Should().Be("single original submission");
                }
                workspace.FindControl<TextBox>("ConversationComposer")!.Text.Should().BeNullOrEmpty();
                compact.FindControl<TextBox>("ConversationComposer")!.Text.Should().BeNullOrEmpty();
            }
            finally { owner.Revoke(); }
        });
    }

    private static void Click(Window window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
