using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentSearchCompletesCrossConversationSelectionAtExactOlderMessageWithNativeFocusAndOriginalActions(bool done)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var originalSession = f.Request.SessionId;
            var original = new List<ConversationMessage>();
            for (var index = 0; index < 27; index++)
            {
                original.Add((await WindowsSqliteConversationTests.Submit(f, originalSession,
                    "needle original " + index)).Message);
            }
            var firstAnswer = await RetainSearchAnswer(f, "needle first retained answer");
            await WindowsSqliteConversationTests.Submit(f, originalSession, "unrelated latest message");
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var secondAnswer = await RetainSearchAnswer(f, "needle second retained answer");
            var secondSession = f.Request.SessionId;
            var playback = new NativeAnswerPlayback();
            var opened = new List<Kora.Core.Presentation.AdmittedDetailContent>();
            await using var composed = new Composition(f, async (content, live, resolve, token) =>
            {
                live().Should().BeTrue();
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                opened.Add(content);
                return "exact original detail";
            }, answerPlayback: playback);
            await composed.Workspace.RenameAsync(originalSession, new(1), 0, new("Same name"), RequestOrigin.LocalUi, f.Token);
            await composed.Workspace.RenameAsync(secondSession, new(1), 0, new("Same name"), RequestOrigin.LocalUi, f.Token);
            if (done)
            { await composed.Workspace.ChangeLifecycleAsync(originalSession, new(1), false, RequestOrigin.LocalUi, f.Token); }
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == secondSession));
                model.ComposerBody = "independent second-topic input";
                var retained = await f.Store.ReadRetentionAsync(originalSession, f.Token);
                var secondRetained = await f.Store.ReadRetentionAsync(secondSession, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var history = (await f.Store.ReadHistoryAsync(originalSession, null, 50, f.Token)).Records;
                window.FindControl<Expander>("ContentSearchPanel")!.IsExpanded = true;
                var query = window.FindControl<TextBox>("ContentQuery")!;
                query.Text = "needle";
                model.ContentMatches.Should().BeEmpty("typing never reads");
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.ConversationOperation;
                var matches = new List<SessionsViewModel.ContentMatch>(model.ContentMatches);
                while (model.CanNextContentSearch)
                {
                    Click(window, "NextContentSearch");
                    await window.ConversationOperation;
                    matches.AddRange(model.ContentMatches);
                }
                matches.Should().HaveCount(29);
                matches.Select(match => match.Target.Authority.SessionId).Distinct().Should().HaveCount(2);
                matches.Should().Contain(match => match.Message == firstAnswer && match.Assistant);
                matches.Should().Contain(match => match.Message == secondAnswer && match.Assistant);
                matches.Should().NotContain(match => match.Text.Contains("latest", StringComparison.Ordinal));
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(secondSession, "search alone never navigates");
                model.ComposerBody.Should().Be("independent second-topic input");
                playback.Calls.Should().Be(0);
                model.ContentQuery = "needle";
                await model.SearchContentAsync();
                while (!model.ContentMatches.Any(match => match.Message == original[26]) && model.CanNextContentSearch)
                { await model.SearchContentAsync(next: true); }
                var exact = model.ContentMatches.Single(match => match.Message == original[26]);
                var results = window.FindControl<ListBox>("ContentMatches")!;
                results.SelectedItem = exact;
                results.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.ConversationOperation;
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(originalSession);
                model.SelectedSessionRecord.Authority.IsActive.Should().Be(!done, "locating retained content does not resume a Done conversation");
                model.ConversationMessages.Should().Equal(original[26]);
                model.SelectedAnswerRecord.Should().Be(original[26]);
                model.ConversationStatus.Should().Contain("Located the exact");
                var messages = window.FindControl<ListBox>("AnswerSourceRecords")!;
                var focused = messages.ContainerFromItem(model.SelectedAnswerRecord) as ListBoxItem;
                focused.Should().NotBeNull();
                focused!.GetVisualDescendants().OfType<TextBlock>().Single(text => string.Equals(text.Name, "OriginalMessageText", StringComparison.Ordinal))
                    .IsFocused.Should().BeTrue("opening must locate and focus the actual retained text, not merely its session");
                await model.OpenConversationDetailAsync(model.ConversationMessages.Single());
                opened.Single().ConversationMessage.Should().Be(original[26]);
                opened.Single().Source.Should().Be(original[26].Body);
                model.ContentQuery = "needle second retained";
                await model.SearchContentAsync();
                while (!model.ContentMatches.Any(match => match.Message == secondAnswer) && model.CanNextContentSearch)
                { await model.SearchContentAsync(next: true); }
                results.SelectedItem = model.ContentMatches.Single(match => match.Message == secondAnswer);
                Click(window, "OpenContentMatch");
                await window.ConversationOperation;
                model.SelectedSessionRecord!.Authority.SessionId.Should().Be(secondSession);
                model.SelectedAnswerRecord.Should().Be(secondAnswer);
                ((ListBoxItem)messages.ContainerFromItem(model.SelectedAnswerRecord)!).GetVisualDescendants()
                    .OfType<TextBlock>().Single(text => string.Equals(text.Name, "RetainedAssistantText", StringComparison.Ordinal)).IsFocused.Should().BeTrue();
                model.ComposerBody.Should().Be("independent second-topic input");
                playback.Calls.Should().Be(0, "locating an answer never starts speech");
                await model.ReadSelectedAnswerAsync();
                playback.Body.Should().Be(secondAnswer.AssistantResponse!.Body);
                playback.Request!.SessionId.Should().Be(secondSession);
                (await f.Store.ReadRetentionAsync(originalSession, f.Token)).Should().Be(retained);
                (await f.Store.ReadRetentionAsync(secondSession, f.Token)).Should().Be(secondRetained);
                (await f.Store.ReadHistoryAsync(originalSession, null, 50, f.Token)).Records.Should().Equal(history);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("conversation_messages").Should().Be(30);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                composed.Audit.Events.Should().BeEmpty("search, navigation and reading grant no model/network authority");
            }
            finally { window.ClosePrivate(); }
            model.ContentMatches.Should().BeEmpty();
            model.ContentQuery.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task ContentSearchContinuesAfterEmptyBoundedPagesAndRefusesForgedOrClearedMatches()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var sessions = new List<SessionWorkspaceEntry>();
            for (var index = 0; index < 4; index++)
            { sessions.Add(await composed.Workspace.CreateAsync(new("Same name"), RequestOrigin.LocalUi, f.Token)); }
            var last = sessions.MaxBy(row => row.Authority.SessionId.Value.ToString("D"), StringComparer.Ordinal)!;
            var request = WindowsSqliteConversationTests.Fresh(last.Authority.SessionId);
            ConversationMessage message;
            using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
            {
                await f.Store.RetainConversationMessageAsync(request, last.Authority.Generation, last.Metadata!.Revision.Value,
                    "needle last conversation", static () => true, f.Token);
                message = (await f.Store.CompleteConversationSubmissionAsync(request, Kora.Core.Dependencies.ModelTurnOutcome.Unavailable,
                    Kora.Core.Dependencies.ModelTurnReason.UnregisteredModel, static () => true, f.Token)).Message;
            }
            var model = composed.Model;
            model.ContentQuery = "needle";
            await model.SearchContentAsync();
            model.ContentMatches.Should().BeEmpty();
            model.CanNextContentSearch.Should().BeTrue("an empty bounded scan is not end-of-history");
            while (model.ContentMatches.Count == 0 && model.CanNextContentSearch)
            { await model.SearchContentAsync(next: true); }
            var match = model.ContentMatches.Single();
            match.Message.Should().Be(message);
            await model.OpenContentMatchAsync(match with { HeadRevision = match.HeadRevision });
            model.SelectedSessionRecord.Should().BeNull("equal caller-created rows are not native selections");
            model.ContentSearchStatus.Should().Contain("actual current");
            model.ClearContentSearch();
            await model.OpenContentMatchAsync(match);
            model.SelectedSessionRecord.Should().BeNull();
            model.ContentMatches.Should().BeEmpty();
            model.ContentQuery.Should().BeEmpty();
            model.CanNextContentSearch.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("lifecycle")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("delete")]
    [InlineData("retention")]
    [InlineData("replace")]
    public async Task ContentSearchStaleSourcesNeverOpenReplacementOrLatestMessage(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var message = await RetainSearchAnswer(f, "needle old answer");
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "unrelated current last message");
            await using var composed = new Composition(f);
            var model = composed.Model;
            model.ContentQuery = "needle";
            await model.SearchContentAsync();
            var match = model.ContentMatches.Single();
            switch (change)
            {
                case "rename":
                    await composed.Workspace.RenameAsync(f.Request.SessionId, new(1), 0, new("Renamed conversation"),
                        RequestOrigin.LocalUi, f.Token);
                    model.ContentMatches.Should().BeEmpty();
                    break;
                case "lifecycle":
                    await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
                    model.ContentMatches.Should().BeEmpty();
                    break;
                case "private": composed.Access.CanInspect = false; break;
                case "control": composed.Access.ControlRevision++; break;
                case "delete":
                    var preview = await f.Store.PreviewDispositionAsync(f.Request.SessionId, new(1), 0, f.Token);
                    var control = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
                    using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
                    {
                        await f.Store.RecordControlIntentAsync(control, f.Token);
                        await f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token);
                    }
                    break;
                case "retention":
                    var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                    f.Time.Now = retention.DeleteDue;
                    using (var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                        HostActivityLayer.Application, HostOperation.Retention))
                    {
                        var batch = await f.Store.ApplyRetentionAsync(static () => true,
                            static (_, _) => ValueTask.CompletedTask, f.Token);
                        batch.Deleted.Should().Be(1);
                    }
                    break;
                case "replace":
                    f.Mutate("UPDATE conversation_messages SET payload=json_set(payload,'$.AssistantResponse.Body','replacement') WHERE sequence=1;");
                    break;
            }
            await model.OpenContentMatchAsync(match);
            model.ConversationMessages.Should().BeEmpty();
            model.SelectedAnswerRecord.Should().BeNull();
            model.ContentSearchStatus.Should().NotContain("Exact message located");
            model.ContentMatches.Should().BeEmpty();
            if (change is "rename")
            {
                model.ContentQuery = "needle";
                await model.SearchContentAsync();
                var fresh = model.ContentMatches.Single();
                fresh.Label.Should().StartWith("Renamed conversation");
                await model.OpenContentMatchAsync(fresh);
                model.ConversationMessages.Should().Equal(message);
                await composed.Workspace.RenameAsync(f.Request.SessionId, new(1), 1, new("Changed again"),
                    RequestOrigin.LocalUi, f.Token);
                model.ConversationMessages.Should().BeEmpty("a located message is retired when its owning source changes");
                model.SelectedAnswerRecord.Should().BeNull();
            }
        });
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("edit")]
    [InlineData("close")]
    [InlineData("private")]
    public async Task ContentSearchJoinsAndSuppressesNoncooperativeLateReadWithoutRetainingQueryOrRows(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "needle retained");
            var snapshot = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<ConversationPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observed = default;
            var reader = new SearchReader((_, _, _, token) =>
            {
                observed = token;
                entered.TrySetResult();
                return new(release.Task);
            });
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            model.ContentQuery = "needle";
            var pending = model.SearchContentAsync();
            await entered.Task;
            if (change is "clear") { model.ClearContentSearch(); }
            if (change is "edit") { model.ContentQuery = "changed"; }
            if (change is "close") { model.Close(); }
            if (change is "private") { composed.Access.CanInspect = false; }
            if (change is not "private") { observed.IsCancellationRequested.Should().BeTrue(); }
            pending.IsCompleted.Should().BeFalse("cancellation is not a joined storage read");
            release.SetResult(snapshot);
            await pending;
            model.ContentMatches.Should().BeEmpty();
            model.CanNextContentSearch.Should().BeFalse();
            model.CanCancelContentSearch.Should().BeFalse();
            model.ContentQuery.Should().Be(change is "edit" ? "changed" : string.Empty);
            f.Count("conversation_messages").Should().Be(1);
            composed.Audit.Events.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentSearchRejectsChangedTerminalSnapshotEvenWhenSelectedMessageBodyRemainsOriginal(bool selectedChanged)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var answer = await RetainSearchAnswer(f, "needle retained answer");
            var pending = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
            using (var root = HostActivity.BeginRoot(pending, HostActivityLayer.Application, HostOperation.Request))
            {
                await f.Store.RetainConversationMessageAsync(pending, new(1), 0, "needle original",
                    static () => true, f.Token);
            }
            await using var composed = new Composition(f);
            var model = composed.Model;
            model.ContentQuery = "needle";
            await model.SearchContentAsync();
            var match = model.ContentMatches.Single(row => selectedChanged ? row.Message.Request == pending : row.Message == answer);
            using (var root = HostActivity.BeginRoot(pending, HostActivityLayer.Application, HostOperation.Request))
            {
                await f.Store.CompleteConversationSubmissionAsync(pending, Kora.Core.Dependencies.ModelTurnOutcome.Unavailable,
                    Kora.Core.Dependencies.ModelTurnReason.UnregisteredModel, static () => true, f.Token);
            }
            await model.OpenContentMatchAsync(match);
            model.ConversationMessages.Should().BeEmpty();
            model.SelectedAnswerRecord.Should().BeNull();
            model.ContentSearchStatus.Should().NotContain("Exact message located");
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%")]
    public async Task ContentSearchInvalidQueriesDoNotReadConversationOrPublishSuccess(string query)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync) { DenyReads = true };
            await using var composed = new Composition(f, conversationReader: reader);
            composed.Model.ContentQuery = query;
            await composed.Model.SearchContentAsync();
            reader.Reads.Should().Be(0);
            composed.Model.ContentSearchStatus.Should().Contain("Invalid query");
            composed.Model.ContentMatches.Should().BeEmpty();
            composed.Model.CanNextContentSearch.Should().BeFalse();
            composed.Audit.Events.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("edit")]
    [InlineData("private")]
    [InlineData("close")]
    public async Task ContentSearchOpeningRevalidatesAfterAwaitAndJoinsWithoutNavigationOrReentrantOpen(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var message = await RetainSearchAnswer(f, "needle original answer");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<ConversationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observed = default;
            var reads = 0;
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, readMetadata: f.Store.ReadMetadataAsync,
                readMessage: (_, _, token) =>
                {
                    reads++;
                    observed = token;
                    entered.TrySetResult();
                    return new(release.Task);
                });
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            model.ContentQuery = "needle";
            await model.SearchContentAsync();
            var match = model.ContentMatches.Single();
            var pending = model.OpenContentMatchAsync(match);
            await entered.Task;
            await model.OpenContentMatchAsync(match);
            reads.Should().Be(1, "a second opening cannot acquire or retarget the in-flight locator");
            if (change is "clear") { model.ClearContentSearch(); }
            if (change is "edit") { model.ContentQuery = "changed"; }
            if (change is "private") { composed.Access.CanInspect = false; }
            if (change is "close") { model.Close(); }
            if (change is not "private") { observed.IsCancellationRequested.Should().BeTrue(); }
            pending.IsCompleted.Should().BeFalse();
            release.SetResult(message);
            await pending;
            model.SelectedSessionRecord.Should().BeNull();
            model.ConversationMessages.Should().BeEmpty();
            model.SelectedAnswerRecord.Should().BeNull();
            model.ContentMatches.Should().BeEmpty();
            model.ContentQuery.Should().Be(change is "edit" ? "changed" : string.Empty);
            model.CanCancelContentSearch.Should().BeFalse();
            composed.Audit.Events.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task ContentSearchReadDenialIsUnavailableRatherThanEmptySuccessfulSearch()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync) { DenyReads = true };
            await using var composed = new Composition(f, conversationReader: reader);
            composed.Model.ContentQuery = "needle";
            await composed.Model.SearchContentAsync();
            reader.Reads.Should().Be(1);
            composed.Model.ContentSearchStatus.Should().Contain("unavailable").And.NotContain("End of");
            composed.Model.ContentMatches.Should().BeEmpty();
            composed.Model.CanNextContentSearch.Should().BeFalse();
            composed.Audit.Events.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task ContentSearchFailedRevalidationRetiresOnlyItsOwnLocatedPageAndNeverShowsChangedAnswer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var message = await RetainSearchAnswer(f, "needle original");
            await using var composed = new Composition(f);
            var model = composed.Model;
            model.ContentQuery = "needle";
            await model.SearchContentAsync();
            var match = model.ContentMatches.Single();
            await model.OpenContentMatchAsync(match);
            model.ConversationMessages.Should().Equal(message);
            f.Mutate("UPDATE conversation_messages SET payload=json_set(payload,'$.AssistantResponse.Body','changed answer');");
            await model.OpenContentMatchAsync(match);
            model.ConversationMessages.Should().BeEmpty();
            model.SelectedAnswerRecord.Should().BeNull();
            model.ContentMatches.Should().BeEmpty();
            model.ConversationStatus.Should().Contain("retired");
        });
    }

    [Fact]
    public async Task ContentSearchContinuationCannotRenewChangedPrivateAdmission()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            for (var index = 0; index < 3; index++)
            { await composed.Workspace.CreateAsync(new("Empty conversation"), RequestOrigin.LocalUi, f.Token); }
            var model = composed.Model;
            model.ContentQuery = "needle";
            await model.SearchContentAsync();
            model.CanNextContentSearch.Should().BeTrue();
            composed.Access.ControlRevision++;
            model.CanNextContentSearch.Should().BeFalse();
            await model.SearchContentAsync(next: true);
            model.ContentSearchStatus.Should().Contain("admission changed");
            model.ContentQuery.Should().BeEmpty();
            model.ContentMatches.Should().BeEmpty();
            model.CanNextContentSearch.Should().BeFalse();
        });
    }
}
