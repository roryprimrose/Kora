using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Storage;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeReviewedSaveUsesPrivateCanonicalRevisionAndCatalogueWithManualReloadAndIndependentImmutableSubmission()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var model = new NewRequestViewModel(composer);
            var window = new NewRequestWindow(model, () => Task.CompletedTask, _ => { });
            window.Show();
            try
            {
                window.FindControl<Button>("ReviewSaveNewRequest")!.IsEnabled.Should().BeFalse();
                window.FindControl<Button>("SaveNewRequest")!.IsEnabled.Should().BeFalse();
                await composer.Create();
                var id = composer.Target!.Authority.SessionId;
                const string old = " named saved OLD e\u0301🙂\r\ncomplete body ";
                await composed.Model.RefreshAsync();
                await composed.Model.SelectAsync(composed.Model.Sessions.Single(row => row.Authority.SessionId == id));
                await composed.Model.ReadConversationAsync();
                composed.Model.ComposerBody = old;
                await composed.Model.SaveConversationDraftAsync();
                var before = await f.Store.ReadRetentionAsync(id, f.Token);
                const string body = " new original NEW e\u0301🙂\r\nexact end ";
                window.FindControl<TextBox>("NewRequestBody")!.Text = body;
                window.FindControl<Button>("ReviewSaveNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.SaveLabel.Should().Contain("Saved revision 1");
                window.FindControl<TextBlock>("SavedBodyReview")!.Text.Should().Be(old);
                window.FindControl<TextBlock>("ProposedSavedBody")!.Text.Should().Be(body);
                model.BaselineBody.Should().BeEmpty();
                window.FindControl<Button>("SaveNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                composer.Dirty.Should().BeFalse();
                composer.Body.Should().Be(body);
                composer.SavedRevision.Should().Be(2);
                composer.CanSubmit.Should().BeFalse();
                var saved = await f.Store.ReadConversationDraftAsync(id, f.Token);
                saved.Draft.Body.Should().Be(body);
                new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
                Encoding.UTF8.GetBytes(saved.Draft.Body!).Should().Equal(Encoding.UTF8.GetBytes(body));
                (await f.Store.ReadRetentionAsync(id, f.Token)).LastMeaningfulActivity.Should().Be(before.LastMeaningfulActivity);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                composed.Audit.Events.Should().BeEmpty("draft saving never calls provider/model admission");
                SavedDraftPage catalogue;
                using (var scope = HostActivity.BeginRoot(new(new(Guid.NewGuid()), id, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                    HostActivityLayer.Desktop, HostOperation.Request))
                { catalogue = await composed.Workspace.ReadSavedDraftCatalogue(SessionListFilter.All, null, static () => true, f.Token); }
                var exact = catalogue.Records.Single(row => row.Session.SessionId == id);
                exact.DraftRevision.Should().Be(2);
                composed.Model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
                (await composed.Model.SelectSavedDraftAsync(exact, static () => true, f.Token)).Should().BeTrue();
                composed.Model.ComposerBody.Should().BeEmpty();
                await composed.Model.ReloadConversationDraftAsync();
                composed.Model.ComposerBody.Should().Be(body);
                composer.Body = "separate immutable submitted USER";
                composer.Review();
                await composer.Submit();
                (await f.Store.ReadConversationDraftAsync(id, f.Token)).Should().Be(saved);
                (await f.Store.ReadConversationPageAsync(id, null, 25, f.Token)).Messages.Single().Body.Should().Be("separate immutable submitted USER");
                composer.Dirty.Should().BeFalse();
                composer.CanReviewSave.Should().BeFalse();
                using var connection = f.OpenRaw();
                using var audit = connection.CreateCommand();
                audit.CommandText = "SELECT group_concat(envelope) FROM security_audit_events;";
                ((string)audit.ExecuteScalar()!).Should().Contain("conversation.draft.save").And.NotContain(old).And.NotContain(body);
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("known-before-review")]
    [InlineData("after-review")]
    [InlineData("metadata")]
    [InlineData("done")]
    public async Task NativeSaveNeverFollowsConcurrentNamedDraftOrChangedFreshTarget(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        using var composer = composed.NewComposer();
        await composer.Create();
        var id = composer.Target!.Authority.SessionId;
        composer.Body = "New request memory";
        await composer.ReviewSave();
        await composed.Model.RefreshAsync();
        await composed.Model.SelectAsync(composed.Model.Sessions.Single(row => row.Authority.SessionId == id));
        await composed.Model.ReadConversationAsync();
        if (change is "metadata") { await composed.Workspace.RenameAsync(id, new(1), 1, new("Explicitly renamed"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token); }
        else if (change is "done") { await composed.Workspace.ChangeLifecycleAsync(id, new(1), false, Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token); }
        else
        {
            composed.Model.ComposerBody = "concurrent named saved body";
            await composed.Model.SaveConversationDraftAsync();
        }
        if (change is "known-before-review") { await composer.ReviewSave(); }
        (await composer.Save()).Should().BeFalse();
        composer.Body.Should().Be("New request memory");
        composer.CanReviewSave.Should().BeFalse();
        await composer.Inspect();
        await composer.ReviewSave();
        composer.CanSave.Should().BeFalse();
        f.Count("conversation_messages").Should().Be(0);
        (await f.Store.ReadConversationDraftAsync(id, f.Token)).Draft.Body.Should().Be(
            change is "metadata" or "done" ? null : "concurrent named saved body");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualNativeDirtyCloseSaveRequiresFrozenOldNewReviewThenExactReceiptWhileCancelRetainsMemory(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var window = new NewRequestWindow(new(composer), () => Task.CompletedTask, _ => { });
            window.Show();
            await composer.Create();
            var id = composer.Target!.Authority.SessionId;
            composer.Body = "close SAVE frozen body e\u0301🙂\r\nexact";
            var decision = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            var review = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (string.Equals(view.Title, "Close isolated New request draft?", StringComparison.Ordinal)) { decision.TrySetResult(view); }
                if (string.Equals(view.Title, "Confirm exact reviewed draft Save only", StringComparison.Ordinal)) { review.TrySetResult(view); }
            });
            window.Close();
            var choice = await decision.Task.WaitAsync(f.Token);
            choice.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "ReviewSaveBeforeClose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var frozen = await review.Task.WaitAsync(f.Token);
            frozen.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
            frozen.GetVisualDescendants().OfType<TextBlock>().Should().Contain(text => text.Text == composer.Body);
            f.Count("conversation_drafts").Should().Be(0);
            frozen.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Name, confirm ? "ConfirmSaveBeforeClose" : "CancelSaveBeforeClose", StringComparison.Ordinal)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Operation;
            window.IsVisible.Should().Be(!confirm);
            (await f.Store.ReadConversationDraftAsync(id, f.Token)).Draft.Body.Should().Be(confirm ? "close SAVE frozen body e\u0301🙂\r\nexact" : null);
            composer.Body.Should().Be(confirm ? string.Empty : "close SAVE frozen body e\u0301🙂\r\nexact");
            f.Count("conversation_messages").Should().Be(0);
            window.ClosePrivate();
        });
    }

    [Theory]
    [InlineData("no-target")]
    [InlineData("held")]
    public async Task NativeDirtyCloseBeforeConfirmedSaveAuthorityOffersOnlyDiscardAndCancel(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var window = new NewRequestWindow(new(composer), () => Task.CompletedTask, _ => { });
            window.Show();
            composer.Body = "memory only";
            if (state is "held")
            {
                await composer.Create();
                await composed.Workspace.RenameAsync(composer.Target!.Authority.SessionId, new(1), 1, new("Changed"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                await composer.Inspect();
            }
            var openedDialog = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (string.Equals(view.Title, "Close isolated New request draft?", StringComparison.Ordinal)) { openedDialog.TrySetResult(view); }
            });
            window.Close();
            var dialog = await openedDialog.Task.WaitAsync(f.Token);
            dialog.GetVisualDescendants().OfType<Button>().Should().NotContain(button => string.Equals(button.Name, "ReviewSaveBeforeClose", StringComparison.Ordinal));
            dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "CancelClose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Operation;
            window.IsVisible.Should().BeTrue();
            composer.Body.Should().Be("memory only");
            f.Count("conversation_drafts").Should().Be(0);
            window.ClosePrivate();
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("same-bytes")]
    [InlineData("audit")]
    [InlineData("private")]
    public async Task CloseSaveFailureOrLateRevocationNeverDefaultsToSuccessfulClose(string revoke)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var checkpoint = new NewRequestCheckpoint();
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var window = new NewRequestWindow(new(composer), () => Task.CompletedTask, _ => { });
            window.Show();
            await composer.Create();
            composer.Body = "save-close recovery";
            window.DecideDraft = () => Task.FromResult(ComposerDraftDecision.Save);
            window.ConfirmDraftSave = () => Task.FromResult(true);
            checkpoint.Action = "conversation.draft.save";
            checkpoint.Commit = () =>
            {
                if (revoke is "edit") { composer.Body = "newer edit"; }
                if (revoke is "same-bytes") { composer.Body = composer.Body; }
                if (revoke is "audit") { throw new IOException("mandatory audit unavailable"); }
                if (revoke is "private") { Avalonia.Threading.Dispatcher.UIThread.Invoke(window.ClosePrivate); }
            };
            window.Close();
            await window.Operation;
            window.IsVisible.Should().Be(revoke is not "private");
            composer.Body.Should().Be(revoke is "private" ? string.Empty : revoke is "edit" ? "newer edit" : "save-close recovery");
            f.Count("conversation_drafts").Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            window.ClosePrivate();
        });
    }
}
