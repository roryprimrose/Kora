using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task SessionsAndCompactActualOriginalRowsShareInertPrivateReuseReviewWithoutImplicitEffects()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "  complete USER e\u0301🙂\r\n<script>inert</script>  ");
            var calls = 0;
            await using var composed = new Composition(f, reuseOriginal: (_, row, admitted, _) =>
            {
                admitted().Should().BeTrue();
                row.Body.Should().Be("  complete USER e\u0301🙂\r\n<script>inert</script>  ");
                calls++;
                return Task.FromResult("Explicit original USER reuse reviewed; no replay.");
            });
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "independent named-session memory";
            var original = model.ConversationMessages.Single();
            var activity = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var tasks = f.Count("host_tasks");
            var audits = f.Count("security_audit_events");
            var owner = new SessionsPresentationOwner(model);
            var sessions = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            sessions.Show(); compact.Show();
            try
            {
                calls.Should().Be(0);
                foreach (var surface in new Window[] { sessions, compact })
                {
                    var button = surface.GetVisualDescendants().OfType<Button>()
                        .Single(control => string.Equals(control.Name, "ReviewOriginalMessageReuse", StringComparison.Ordinal));
                    button.DataContext.Should().BeSameAs(original);
                    button.IsEnabled.Should().BeTrue();
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await owner.ReuseOperation;
                }
                calls.Should().Be(2);
                await model.ReviewOriginalMessageReuseAsync(original with { });
                calls.Should().Be(2, "value-equal reconstructed records are not admitted native rows");
                model.ComposerBody.Should().Be("independent named-session memory");
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(activity);
                (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, original.Request.RequestId, f.Token)).Should().Be(original);
            }
            finally { owner.Revoke(); }
        });
    }

    [Fact]
    public async Task NativeFullBodyReviewRequiresSeparateReplaceAndRetiresPaneTextAndTransferCallbacksOnCancel()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "  exact original e\u0301🙂\r\n");
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            composer.Body = "keep existing New request memory";
            var source = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var original = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
            using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
            using var review = await OriginalMessageReuseReview.ReviewAsync(composed.Workspace, composed.Access,
                source, original, composer, () => true, f.Token);
            var owner = new Window();
            owner.Show();
            var dialog = new OriginalMessageReuseWindow(owner, review);
            try
            {
                var decision = dialog.Open(owner);
                var fields = dialog.GetLogicalDescendants().OfType<TextBox>().Where(control => control.IsReadOnly).ToArray();
                fields.Select(control => control.Text).Should().Contain(original.Body).And.Contain(composer.Body);
                fields.Should().OnlyContain(control => control.ContextMenu == null && !control.IsUndoEnabled);
                var copy = new RoutedEventArgs(TextBox.CopyingToClipboardEvent);
                fields[0].RaiseEvent(copy);
                copy.Handled.Should().BeTrue();
                dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ConfirmOriginalMessageReuse", StringComparison.Ordinal))
                    .Content!.ToString().Should().Contain("Replace");
                dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "CancelOriginalMessageReuse", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await decision).Should().BeFalse();
                review.Available.Should().BeFalse();
                fields.Should().OnlyContain(control => string.IsNullOrEmpty(control.Text));
                composer.Body.Should().Be("keep existing New request memory");
                (await review.ConfirmAsync(true, f.Token)).Should().BeFalse();
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("selection")]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("control")]
    public async Task SourceSearchAndPrivateChangesRetireLateReuseCallbackWithoutRetargetingOrBufferLoss(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker original");
        Func<bool>? stillAdmitted = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var composed = new Composition(f, reuseOriginal: (_, _, admitted, _) =>
        {
            stillAdmitted = admitted;
            entered.SetResult();
            return release.Task;
        });
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "independent source-session memory";
        model.ConversationQuery = "marker";
        await model.SearchConversationAsync();
        var pending = model.ReviewOriginalMessageReuseAsync(model.ConversationSearchMessages.Single());
        await entered.Task;
        switch (change)
        {
            case "query": model.ConversationQuery = "changed"; break;
            case "selection": model.RevokeSessionList(); model.ClearConversationSearch(); break;
            case "close": model.Close(); break;
            case "private": composed.Access.CanInspect = false; break;
            case "control": composed.Access.ControlRevision++; break;
        }
        stillAdmitted!().Should().BeFalse();
        release.SetResult("late callback must not transfer any text");
        await pending;
        f.Count("conversation_messages").Should().Be(1);
        f.Count("conversation_drafts").Should().Be(0);
        if (change is "query" or "selection") { model.ComposerBody.Should().Be("independent source-session memory"); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("tombstone")]
    public async Task MissingCorruptOrTombstonedRealStoreSourceCannotCompletePreviouslyReviewedReuse(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "immutable retained USER");
        await using var composed = new Composition(f);
        using var composer = composed.NewComposer();
        composer.Body = "independent memory to keep";
        var source = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var original = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        using var review = await OriginalMessageReuseReview.ReviewAsync(composed.Workspace, composed.Access,
            source, original, composer, () => true, f.Token);
        if (fault is "tombstone")
        {
            var preview = await composed.Workspace.PreviewDispositionAsync(source.Authority.SessionId,
                source.Authority.Generation, source.Metadata?.Revision.Value ?? 0, f.Token);
            await composed.Workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, f.Token);
            (await review.ConfirmAsync(true, f.Token)).Should().BeFalse();
        }
        else
        {
            f.Mutate(fault is "missing" ? "DELETE FROM conversation_messages;"
                : "UPDATE conversation_messages SET payload=json_set(payload,'$.Body','substituted');");
            Func<Task> confirm = () => review.ConfirmAsync(true, f.Token);
            await confirm.Should().ThrowAsync<Exception>();
        }
        review.Available.Should().BeFalse();
        review.Body.Should().BeEmpty();
        composer.Body.Should().Be("independent memory to keep");
        composer.Creation.Should().BeNull();
        composer.Submission.Should().BeNull();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("control")]
    public async Task ReentrantOriginalReviewOpeningRevocationErasesPanesAndFencesRetainedConfirmButton(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "historical private USER");
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var source = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var original = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
            using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
            using var review = await OriginalMessageReuseReview.ReviewAsync(composed.Workspace, composed.Access,
                source, original, composer, () => true, f.Token);
            var owner = new Window();
            owner.Show();
            var dialog = new OriginalMessageReuseWindow(owner, review);
            TextBox[] fields = [];
            Button? retainedConfirm = null;
            using var opening = Window.WindowOpenedEvent.AddClassHandler<OriginalMessageReuseWindow>((view, _) =>
            {
                fields = view.GetLogicalDescendants().OfType<TextBox>().Where(control => control.IsReadOnly).ToArray();
                retainedConfirm = view.GetLogicalDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ConfirmOriginalMessageReuse", StringComparison.Ordinal));
                if (change is "source") { review.Dispose(); }
                else { composed.Access.ControlRevision++; }
            });
            try
            {
                (await dialog.Open(owner)).Should().BeFalse();
                fields.Should().HaveCount(2).And.OnlyContain(control => string.IsNullOrEmpty(control.Text));
                retainedConfirm!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await review.ConfirmAsync(true, f.Token)).Should().BeFalse();
                composer.Body.Should().BeEmpty();
                composer.Creation.Should().BeNull();
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }
}
