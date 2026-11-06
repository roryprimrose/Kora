using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("unavailable")]
    [InlineData("changed")]
    [InlineData("unchanged")]
    public Task ConversationRefreshRetiresUnconfirmedReaderAndFindBeforeReturningWithoutChangingRetainedData(string outcome) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "exact private reader marker");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshing = false;
            var reader = new SearchReader(async (session, cursor, limit, token) =>
            {
                if (refreshing)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                    if (outcome is "unavailable") { throw new IOException("Synthetic conversation source unavailable."); }
                    if (outcome is "changed") { throw new ConversationSnapshotChangedException(); }
                }
                return await f.Store.ReadConversationPageAsync(session, cursor, limit, token);
            }, readMetadata: f.Store.ReadMetadataAsync, readMessage: f.Store.ReadConversationMessageAsync);
            var clipboard = new ReaderRetirementClipboard();
            var owner = new Window();
            DetailWindow? window = null;
            DetailViewerState? state = null;
            using var details = new DetailWindowController(new EmbeddedUserDocumentationProvider(), () => true,
                new(NullLogger<NativeDetailRenderer>.Instance), clipboard, (observed, result, copy) =>
                {
                    state = observed;
                    return window = new DetailWindow(observed, result, copy);
                }, NullLogger<DetailWindowController>.Instance);
            await using var composed = new Composition(f,
                (content, admitted, resolve, token) => details.OpenConversationDetailAsync(content, owner, admitted, resolve, token),
                conversationReader: reader, revokeDetails: details.RevokeConversationSession);
            var model = composed.Model;
            owner.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                var original = model.ConversationMessages.Single();
                model.ComposerBody = "independent unsent input";
                await model.OpenConversationDetailAsync(original);
                window.Should().NotBeNull(model.ConversationStatus);
                var text = window!.FindControl<TextBox>("ContentReader")!;
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                text.SelectionStart = 0;
                text.SelectionEnd = 5;
                var query = window.FindControl<TextBox>("SearchInput")!;
                query.Text = "marker";
                window.FindControl<Button>("FirstMatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state!.MatchCount.Should().Be(1);
                var generation = state.Generation;
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var tasks = f.Count("host_tasks");
                refreshing = true;
                var refresh = model.ReadConversationAsync();
                await entered.Task.WaitAsync(f.Token);
                text.Text.Should().Be(original.Body, "an outstanding refresh alone is not source loss");
                release.SetResult();
                await refresh;
                if (outcome is "unchanged")
                {
                    window.IsVisible.Should().BeTrue();
                    state.Content.Should().NotBeNull();
                    query.Text.Should().Be("marker");
                    state.MatchCount.Should().Be(1);
                    text.SelectedText.Should().Be("exact");
                    model.ConversationMessages.Should().ContainSingle();
                }
                else
                {
                    window.IsVisible.Should().BeFalse("known source loss must retire the reader before refresh completes, not on a later timer");
                    state.Content.Should().BeNull();
                    text.Text.Should().BeEmpty();
                    text.SelectedText.Should().BeEmpty();
                    query.Text.Should().BeEmpty();
                    state.MatchCount.Should().BeNull();
                    state.MatchOrdinal.Should().BeNull();
                    state.CompleteRender(generation, "late private body", "Rendered", false).Should().BeFalse();
                    window.FindControl<Button>("FirstMatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await details.CopyAsync(state.Reference, generation, true, 0, 5);
                    state.MatchStart.Should().Be(-1);
                    model.ConversationMessages.Should().BeEmpty();
                }
                model.ComposerBody.Should().Be("independent unsent input");
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
                (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, original.Request.RequestId, f.Token)).Should().Be(original);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                clipboard.Writes.Should().Be(0);
            }
            finally
            {
                release.TrySetResult();
                details.ClearForPrivacy();
                owner.Close();
            }
        });

    private sealed class ReaderRetirementClipboard : IDetailClipboard
    {
        internal int Writes { get; private set; }
        public Task WriteAsync(IDetailView view, string source, string? htmlFragment)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("query")]
    [InlineData("role")]
    public async Task CriteriaEditsFenceLateDetailCallbacksWithoutClearingIndependentComposer(string edit)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker original");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var composed = new Composition(f, (_, _, _, _) =>
        {
            entered.SetResult();
            return release.Task;
        });
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "independent memory draft";
        model.ConversationQuery = "marker";
        await model.SearchConversationAsync();
        var pending = model.OpenConversationDetailAsync(model.ConversationSearchMessages.Single());
        await entered.Task;
        EditSearchCriteria(model, edit);
        release.SetResult("stale success must not publish");
        await pending;
        model.ConversationStatus.Should().Contain("navigation revoked").And.NotContain("stale success");
        model.ConversationSearchMessages.Should().BeEmpty();
        model.ConversationRole.Should().Be(edit is "role" ? ConversationSearchRole.HOST : ConversationSearchRole.All);
        model.ConversationQuery.Should().Be(edit is "query" ? "different" : "marker");
        model.ComposerBody.Should().Be("independent memory draft");
    }

    [Theory]
    [InlineData("query")]
    [InlineData("role")]
    public async Task SearchCriteriaEditRevokesMatchDetailsAndDeferredNavigationWithoutTouchingComposer(string edit)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker original");
        Func<bool>? admitted = null;
        Func<CancellationToken, Task<AdmittedDetailContent>>? revalidate = null;
        var revoked = new List<HostId<SessionIdentity>>();
        await using var composed = new Composition(f, (content, live, resolve, _) =>
        {
            live().Should().BeTrue();
            admitted = live;
            revalidate = resolve;
            return Task.FromResult("Original search match Details opened");
        }, revokeDetails: revoked.Add);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "independent memory draft";
        model.ConversationQuery = "marker";
        await model.SearchConversationAsync();
        await model.OpenConversationDetailAsync(model.ConversationSearchMessages.Single());
        admitted!().Should().BeTrue();
        EditSearchCriteria(model, edit);
        admitted().Should().BeFalse();
        revoked.Should().Equal(f.Request.SessionId);
        Func<Task> resolveAgain = () => revalidate!(f.Token);
        await resolveAgain.Should().ThrowAsync<InvalidOperationException>();
        model.ConversationSearchMessages.Should().BeEmpty();
        model.ComposerBody.Should().Be("independent memory draft");
    }

    private static void EditSearchCriteria(SessionsViewModel model, string edit)
    {
        switch (edit)
        {
            case "role": model.ConversationRole = ConversationSearchRole.HOST; break;
            default: model.ConversationQuery = "different"; break;
        }
    }

    [Fact]
    public async Task SessionsAndCompactShareExplicitRetainedDetailsWithDoneStillPassiveAndStaleRowsRefused()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var opened = new List<AdmittedDetailContent>();
            Func<CancellationToken, Task<AdmittedDetailContent>>? revalidate = null;
            await using var composed = new Composition(f, async (content, admitted, resolve, token) =>
            {
                admitted().Should().BeTrue();
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                opened.Add(content);
                revalidate = resolve;
                return "Exact original USER Details opened; separate HOST.";
            });
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "  exact USER e\u0301 😀\r\n\t<script>inert</script>  ";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            var original = model.ConversationMessages.Single();
            using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
            {
                await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId,
                    model.SelectedSessionRecord!.Authority.Generation, false, RequestOrigin.LocalUi, f.Token);
            }
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            var retained = model.ConversationMessages.Single();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var owner = new SessionsPresentationOwner(model);
            var sessions = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            sessions.Show();
            compact.Show();
            try
            {
                opened.Should().BeEmpty("opening either surface does not automatically open Details");
                foreach (var window in new Window[] { sessions, compact })
                {
                    var button = window.GetVisualDescendants().OfType<Button>()
                        .Single(control => string.Equals(control.Name, "OpenConversationDetail", StringComparison.Ordinal));
                    button.DataContext.Should().BeSameAs(retained);
                    button.IsEnabled.Should().BeTrue();
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await owner.DetailOperation;
                }
                opened.Should().HaveCount(2);
                (await revalidate!(f.Token)).ConversationMessage.Should().Be(retained,
                    "deferred source revalidation uses a fresh linked host trace, not the retired opening activity");
                opened.Should().OnlyContain(content => string.Equals(content.Source, retained.Body, StringComparison.Ordinal)
                    && content.ConversationMessage == retained && content.SessionSource == null);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                model.SelectedSessionRecord!.Authority.IsActive.Should().BeFalse();
                await model.OpenConversationDetailAsync(original);
                opened.Should().HaveCount(2, "an old row from a different selected generation cannot open");
                model.ConversationStatus.Should().Contain("unavailable or changed");
            }
            finally { owner.Revoke(); }
        });
    }

    [Fact]
    public async Task ExactStoreResolverRefusesForeignMissingDeletedAndTamperedOriginalRecords()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "exact original";
        await model.SubmitConversationAsync();
        await model.ReadConversationAsync();
        var record = model.ConversationMessages.Single();
        using var root = HostActivity.BeginRoot(record.Request, HostActivityLayer.Desktop, HostOperation.Request);
        (await composed.Conversation.ReadDetailAsync(model.SelectedSessionRecord!, record, () => true, f.Token))
            .ConversationMessage.Should().Be(record);
        await composed.Conversation.Invoking(service => service.ReadDetailAsync(model.SelectedSessionRecord!,
            record with { Body = "fabricated" }, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        await f.Store.Invoking(store => store.ReadConversationMessageAsync(f.Request.SessionId,
            new(Guid.NewGuid()), f.Token).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        await f.Store.Invoking(store => store.ReadConversationMessageAsync(f.Request.SessionId,
            record.Request.RequestId, f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
        var target = model.SelectedSessionRecord!;
        var preview = await composed.Workspace.PreviewDispositionAsync(target.Authority.SessionId,
            target.Authority.Generation, target.Metadata?.Revision.Value ?? 0, f.Token);
        await composed.Workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, f.Token);
        await f.Store.Invoking(store => store.ReadConversationMessageAsync(target.Authority.SessionId,
            record.Request.RequestId, f.Token).AsTask()).Should().ThrowAsync<InvalidOperationException>();
    }
}
