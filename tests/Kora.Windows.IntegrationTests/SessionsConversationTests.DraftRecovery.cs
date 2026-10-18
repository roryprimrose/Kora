using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDraftRecoveryReviewsCompleteOldLatestRawAndOnlyAdoptsBaselineThenSaveCanConflictAgain(bool compactRoute)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            const string old = "  OLD e\u0301\U0001f642\r\n\t ";
            const string raw = "  RAW e\u0301\U0001f642\r\n <script>inert</script> \t ";
            const string latestBody = " LATEST \r\n e\u0301\U0001f642 ";
            var saved = await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), old);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show(); compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = raw;
                var latest = await WindowsSqliteConversationTests.Save(f, saved, latestBody);
                await model.SaveConversationDraftAsync();
                model.ConversationStatus.Should().Contain("Unconfirmed");
                model.CanRecoverConversationDraft.Should().BeTrue("an exact optimistic conflict is not an uncertain successful commit");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var auditCount = composed.Audit.Events.Count;
                var tasks = f.Count("host_tasks");
                var (dialog, operation) = await BeginDraftRecovery(compactRoute ? compact : workspace);
                var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                panes.Should().HaveCount(3);
                CompareSource(panes[0]).Text.Should().Be(old);
                CompareSource(panes[1]).Text.Should().Be(latestBody);
                CompareSource(panes[2]).Text.Should().Be(raw);
                foreach (var pane in panes)
                {
                    var copy = new RoutedEventArgs(TextBox.CopyingToClipboardEvent);
                    CompareSource(pane).RaiseEvent(copy);
                    copy.Handled.Should().BeTrue();
                    CompareSource(pane).IsReadOnly.Should().BeTrue();
                    CompareSource(pane).IsUndoEnabled.Should().BeFalse();
                    CompareSource(pane).ContextMenu.Should().BeNull();
                }
                RecoveryClick(dialog, "Adopt latest baseline only");
                await operation;
                model.ConversationStatus.Should().Contain("adopted in memory only");
                model.ComposerBody.Should().Be(raw);
                model.ComposerDirty.Should().BeTrue();
                model.ComposerTarget.Should().Contain("saved draft revision 2");
                workspace.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(raw);
                compact.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(raw);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                composed.Audit.Events.Count.Should().Be(auditCount);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("conversation_messages").Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Should().Be(latest.Draft);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                AssertRecoveryErased(dialog, panes);
                using var passive = model.CompareComposer();
                passive!.SavedBody.Should().Be(latestBody, "existing Compare remains historical and passive");
                var raced = await WindowsSqliteConversationTests.Save(f, latest, "changed after adoption");
                await model.SaveConversationDraftAsync();
                model.ConversationStatus.Should().Contain("Unconfirmed");
                model.ComposerBody.Should().Be(raw);
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Should().Be(raced.Draft);
                await model.ReviewConversationDraftRecoveryAsync((_, _) => Task.FromResult(true), () => true);
                await model.SaveConversationDraftAsync();
                model.ComposerBody.Should().Be(raw);
                model.ComposerDirty.Should().BeFalse();
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().Be(raw);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("same")]
    [InlineData("absent")]
    [InlineData("empty")]
    [InlineData("removed")]
    public async Task NativeRecoveryDistinguishesAbsentEmptyUnchangedAndRemovedSavedBodies(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var saved = await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token);
            if (state is not "absent") { saved = await WindowsSqliteConversationTests.Save(f, saved, state is "empty" ? string.Empty : "old"); }
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "raw";
                var latest = state is "removed" ? await WindowsSqliteConversationTests.Save(f, saved, null) : saved;
                var (dialog, operation) = await BeginDraftRecovery(window);
                var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                CompareSource(panes[0]).Text.Should().Be(saved.Draft.Body ?? string.Empty);
                CompareQuery(panes[0]).IsEnabled.Should().Be(saved.Draft.Body is not null);
                CompareSource(panes[1]).Text.Should().Be(latest.Draft.Body ?? string.Empty);
                CompareQuery(panes[1]).IsEnabled.Should().Be(latest.Draft.Body is not null);
                RecoveryClick(dialog, "Adopt latest baseline only");
                await operation;
                model.ComposerBody.Should().Be("raw");
                model.ComposerTarget.Should().Contain("saved draft revision " + latest.Draft.Revision);
                model.ConversationStatus.Should().Contain("adopted in memory only");
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("escape")]
    [InlineData("close")]
    [InlineData("readback-conflict")]
    [InlineData("aba")]
    public async Task CancelOrChangedReadbackKeepsPriorBaselineExpectedRevisionAndExactRawBuffer(string outcome)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var saved = await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "old");
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "exact raw";
                var target = model.ComposerTarget;
                var latest = await WindowsSqliteConversationTests.Save(f, saved, "latest");
                var (dialog, operation) = await BeginDraftRecovery(window);
                var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                if (outcome is "readback-conflict" or "aba")
                {
                    var changed = await WindowsSqliteConversationTests.Save(f, latest, "intervening");
                    if (outcome is "aba") { await WindowsSqliteConversationTests.Save(f, changed, "latest"); }
                    RecoveryClick(dialog, "Adopt latest baseline only");
                }
                else if (outcome is "cancel") { RecoveryClick(dialog, "Cancel"); }
                else if (outcome is "escape") { dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); }
                else { dialog.Close(); }
                await operation;
                model.ComposerTarget.Should().Be(target);
                model.ComposerBody.Should().Be("exact raw");
                model.ComposerDirty.Should().BeTrue();
                using var comparison = model.CompareComposer();
                comparison!.SavedBody.Should().Be("old");
                model.ConversationStatus.Should().Contain(outcome is "readback-conflict" or "aba" ? "changed during review" : "cancelled");
                AssertRecoveryErased(dialog, panes);
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("same-edit")]
    [InlineData("buffer-aba")]
    [InlineData("compact-edit")]
    [InlineData("control")]
    [InlineData("permission")]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("selection")]
    [InlineData("read")]
    [InlineData("save")]
    [InlineData("submit")]
    [InlineData("protect")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("reentrant")]
    public async Task EveryComposerRecoveryLifetimeFenceRetiresReviewAndRetainedCallbacks(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var session = f.Request.SessionId;
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(session, f.Token), "PRIVATE old");
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            window.Show(); compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == session));
                await model.ReadConversationAsync();
                model.ComposerBody = "PRIVATE raw";
                var (dialog, operation) = await BeginDraftRecovery(window);
                var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                CompareQuery(panes[2]).Text = "PRIVATE";
                CompareClick(panes[2], "Find first");
                model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
                switch (change)
                {
                    case "edit": model.ComposerBody = "edited"; break;
                    case "same-edit": model.ComposerBody = "PRIVATE raw"; break;
                    case "buffer-aba": model.ComposerBody = "edited"; model.ComposerBody = "PRIVATE raw"; break;
                    case "compact-edit": compact.FindControl<TextBox>("ConversationComposer")!.Text = "compact edit"; break;
                    case "control": composed.Access.ControlRevision++; break;
                    case "permission": composed.Access.CanControl = false; break;
                    case "privacy": composed.Access.CanInspect = false; break;
                    case "source": model.RevokeSessionList(); break;
                    case "generation":
                        await composed.Workspace.ChangeLifecycleAsync(session, new(1), false, RequestOrigin.LocalUi, f.Token);
                        await model.RefreshWorkAsync();
                        break;
                    case "selection": await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != session)); break;
                    case "read": await model.ReadConversationAsync(); break;
                    case "save": await model.SaveConversationDraftAsync(); break;
                    case "submit": await model.SubmitConversationAsync(); break;
                    case "protect": await model.ProtectComposerAsync(); break;
                    case "cancel": model.CancelConversationDraftRecovery(); break;
                    case "close": window.Close(); break;
                    case "reentrant": await model.ReviewConversationDraftRecoveryAsync((_, _) => throw new InvalidOperationException("No replacement review."), () => true); break;
                }
                CompareClick(panes[2], "Find next");
                await operation;
                AssertRecoveryErased(dialog, panes);
                CompareQuery(panes[2]).Text = "late callback";
                CompareClick(panes[2], "Find first");
                CompareSource(panes[2]).Text.Should().BeEmpty();
                if (change is "edit" or "compact-edit") { model.ComposerBody.Should().Be(change is "edit" ? "edited" : "compact edit"); }
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("close")]
    [InlineData("confirm-edit")]
    public async Task PendingSourceReadsCannotResurrectReviewOrBaselineAfterRetirement(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var reads = 0;
            var pause = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask<ConversationDraftSnapshot> ReadDraft(HostId<SessionIdentity> session, CancellationToken token)
            {
                reads++;
                var observed = await f.Store.ReadConversationDraftAsync(session, token);
                if (reads == pause) { entered.TrySetResult(); await release.Task; }
                return observed;
            }
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, ReadDraft, f.Store.ReadMetadataAsync);
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "raw";
            var target = model.ComposerTarget;
            var reviews = 0;
            pause = change is "confirm-edit" ? 3 : 2;
            var operation = model.ReviewConversationDraftRecoveryAsync((_, _) =>
            {
                reviews++;
                return Task.FromResult(true);
            }, () => true);
            await entered.Task;
            if (change is "edit" or "confirm-edit") { model.ComposerBody = "edited"; }
            if (change is "cancel") { model.CancelConversationDraftRecovery(); }
            if (change is "source") { model.RevokeSessionList(); }
            if (change is "close") { model.Close(); }
            release.TrySetResult();
            await operation;
            reviews.Should().Be(change is "confirm-edit" ? 1 : 0);
            if (change is not "close") { model.ComposerTarget.Should().Be(target); }
            if (change is "edit" or "confirm-edit") { model.ComposerBody.Should().Be("edited"); }
            f.Count("conversation_drafts").Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
        });
    }

    [Fact]
    public async Task FieldEditsAndUncertainSaveCannotReadOrAdoptUntilExplicitExactReconciliation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync, f.Store.ReadMetadataAsync);
        await using var composed = new Composition(f, conversationReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        reader.DenyReads = true;
        model.ComposerBody = "raw";
        model.ComposerBody = "raw";
        reader.DraftReads.Should().Be(1);
        reader.Reads.Should().Be(1);
        model.ReportComposerCloseFailure(new IOException("Uncertain previous save receipt."));
        model.CanRecoverConversationDraft.Should().BeFalse();
        await model.ReviewConversationDraftRecoveryAsync((_, _) => throw new InvalidOperationException("No uncertain review."), () => true);
        await model.SaveConversationDraftAsync();
        reader.DraftReads.Should().Be(1);
        model.ComposerBody.Should().Be("raw");
        reader.DenyReads = false;
        await model.ReadConversationAsync();
        model.CanRecoverConversationDraft.Should().BeTrue();
        model.ComposerBody.Should().Be("raw");
        reader.DenyReads = true;
        await model.ReviewConversationDraftRecoveryAsync((_, _) => throw new InvalidOperationException("No failed-source review."), () => true);
        model.CanRecoverConversationDraft.Should().BeFalse("unknown source needs an explicit exact read, not manufactured certainty");
        model.ComposerBody.Should().Be("raw");
        model.ComposerBody = "corrected raw";
        reader.DraftReads.Should().Be(3, "an edit cannot secretly read/reconcile the source");
        reader.DenyReads = false;
        await model.ReadConversationAsync();
        model.CanRecoverConversationDraft.Should().BeTrue();
        model.ComposerBody.Should().Be("corrected raw");
    }

    [Fact]
    public async Task ReentrantNativeOpeningEditErasesSourcesAndCannotAdoptOrReopen()
    {
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
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "PRIVATE raw";
                ConversationDraftRecoveryWindow? review = null;
                SnapshotComparisonFindPane[] panes = [];
                using var registration = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftRecoveryWindow>((dialog, _) =>
                {
                    review = dialog;
                    panes = dialog.GetLogicalDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                    model.ComposerBody = "edited during Opened";
                });
                window.FindControl<Button>("RecoverConversationDraft")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                review.Should().NotBeNull();
                panes.Should().HaveCount(3);
                AssertRecoveryErased(review!, panes);
                model.ComposerBody.Should().Be("edited during Opened");
                model.ComposerTarget.Should().Contain("saved draft revision 0");
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }
            finally { window.ClosePrivate(); }
        });
    }

    private static async Task<(ConversationDraftRecoveryWindow Dialog, Task Operation)> BeginDraftRecovery(Window window)
    {
        var opened = new TaskCompletionSource<ConversationDraftRecoveryWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftRecoveryWindow>((dialog, _) => opened.TrySetResult(dialog));
        window.FindControl<Button>("RecoverConversationDraft")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var operation = window is SessionsWindow workspace ? workspace.ConversationOperation : ((CompactSessionWindow)window).ConversationOperation;
        (await Task.WhenAny(opened.Task, operation)).Should().Be(opened.Task, "the explicit source read must open the exact review, not fail silently");
        return (await opened.Task, operation);
    }

    private static void RecoveryClick(Window dialog, string label) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void AssertRecoveryErased(Window dialog, SnapshotComparisonFindPane[] panes)
    {
        dialog.IsVisible.Should().BeFalse();
        dialog.Content.Should().BeNull();
        foreach (var pane in panes)
        {
            CompareSource(pane).Text.Should().BeEmpty();
            CompareQuery(pane).Text.Should().BeEmpty();
        }
    }
}
